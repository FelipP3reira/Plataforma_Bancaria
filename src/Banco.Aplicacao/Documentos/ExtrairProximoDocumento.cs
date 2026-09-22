using Banco.Aplicacao.Portas;
using Banco.Dominio.Documentos;
using Microsoft.Extensions.Logging;

namespace Banco.Aplicacao.Documentos;

/// <summary>
/// Tira um documento da fila, manda extrair e grava o resultado.
/// </summary>
/// <remarks>
/// Uma rodada por chamada, e nao um laco interno: quem decide o ritmo e o host, que e o
/// unico que sabe se esta desligando. Laco aqui dentro tornaria o desligamento gracioso
/// impossivel de escrever.
/// </remarks>
public sealed partial class ExtrairProximoDocumento
{
    private readonly IRepositorioDeDocumentos documentos;
    private readonly IArmazenamentoDeDocumentos armazenamento;
    private readonly IExtratorDeDocumento extrator;
    private readonly IUnidadeDeTrabalho unidade;
    private readonly TimeProvider relogio;
    private readonly OpcoesDoPipeline opcoes;
    private readonly ILogger<ExtrairProximoDocumento> registro;

    public ExtrairProximoDocumento(
        IRepositorioDeDocumentos documentos,
        IArmazenamentoDeDocumentos armazenamento,
        IExtratorDeDocumento extrator,
        IUnidadeDeTrabalho unidade,
        TimeProvider relogio,
        OpcoesDoPipeline opcoes,
        ILogger<ExtrairProximoDocumento> registro)
    {
        this.documentos = documentos;
        this.armazenamento = armazenamento;
        this.extrator = extrator;
        this.unidade = unidade;
        this.relogio = relogio;
        this.opcoes = opcoes;
        this.registro = registro;
    }

    /// <returns>Falso quando a fila estava vazia.</returns>
    public async Task<bool> Executar(CancellationToken cancelamento)
    {
        var documento = await Reservar(cancelamento).ConfigureAwait(false);

        if (documento is null)
        {
            return false;
        }

        // A extracao roda fora de qualquer transacao. Chamada a servico de terceiro leva
        // segundos, e transacao aberta nesse tempo segura trava de linha — o que faria uma
        // extracao lenta travar qualquer outra escrita que encostasse no mesmo documento.
        try
        {
            var texto = await Ler(documento, cancelamento).ConfigureAwait(false);

            documento.Concluir(texto.Conteudo, texto.Confianca, relogio.GetUtcNow());

            // Nem o texto nem o tamanho dele: o comprimento de um campo extraido ja diz se
            // o boleto tem valor de tres ou de seis digitos.
            RegistrarExtracao(documento.Id, texto.Confianca);
        }
        catch (Exception erro) when (erro is not OperationCanceledException)
        {
            // O que vai para a coluna e o tipo da excecao, nunca a mensagem: mensagem de
            // erro de leitura costuma citar o trecho que nao entendeu, e esse trecho e
            // conteudo de boleto.
            documento.Falhar(erro.GetType().Name, relogio.GetUtcNow());

            RegistrarFalha(erro, documento.Id, documento.Tentativas, documento.Estado);
        }

        await unidade.Salvar(cancelamento).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Recupera os documentos de worker derrubado e devolve quantos voltaram.
    /// </summary>
    /// <remarks>
    /// A reserva vencida gasta uma tentativa, e nao volta de graca. Documento que derruba o
    /// processo derrubaria o proximo tambem — sem gastar tentativa, ele reiniciaria o worker
    /// em laco e nenhum outro documento andaria.
    /// </remarks>
    public async Task<int> RecuperarPresos(CancellationToken cancelamento)
    {
        await using var transacao = await unidade.Abrir(cancelamento).ConfigureAwait(false);

        var agora = relogio.GetUtcNow();
        var presos = await documentos
            .ComLeaseVencido(agora, opcoes.LotesDeRecuperacao, cancelamento)
            .ConfigureAwait(false);

        foreach (var documento in presos)
        {
            documento.Falhar("Reserva vencida: o processamento nao terminou no prazo.", agora);

            RegistrarRecuperacao(documento.Id, documento.Tentativas, documento.Estado);
        }

        if (presos.Count > 0)
        {
            await unidade.Salvar(cancelamento).ConfigureAwait(false);
        }

        await transacao.Confirmar(cancelamento).ConfigureAwait(false);

        return presos.Count;
    }

    /// <remarks>
    /// A reserva e confirmada antes de a extracao comecar, e nao junto com o resultado. Numa
    /// transacao so, o documento continuaria disponivel na fila durante toda a extracao — e
    /// outro worker o pegaria.
    /// </remarks>
    private async Task<Documento?> Reservar(CancellationToken cancelamento)
    {
        await using var transacao = await unidade.Abrir(cancelamento).ConfigureAwait(false);

        var documento = await documentos.ProximoDaFila(cancelamento).ConfigureAwait(false);

        if (documento is null)
        {
            return null;
        }

        documento.Reservar(relogio.GetUtcNow(), opcoes.PrazoDoLease);

        await unidade.Salvar(cancelamento).ConfigureAwait(false);
        await transacao.Confirmar(cancelamento).ConfigureAwait(false);

        return documento;
    }

    private async Task<TextoDoDocumento> Ler(Documento documento, CancellationToken cancelamento)
    {
        await using var conteudo = await armazenamento
            .Abrir(documento.CaminhoRelativo, cancelamento)
            .ConfigureAwait(false)
            ?? throw new FileNotFoundException("Os bytes do documento nao estao no armazenamento.");

        return await extrator.Extrair(conteudo, documento.Tipo, cancelamento).ConfigureAwait(false);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Documento {Documento} extraido com confianca {Confianca}")]
    private partial void RegistrarExtracao(Guid documento, decimal confianca);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Extracao do documento {Documento} falhou na tentativa {Tentativas}; ficou em {Estado}")]
    private partial void RegistrarFalha(Exception erro, Guid documento, int tentativas, EstadoDoDocumento estado);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Reserva vencida do documento {Documento} recuperada na tentativa {Tentativas}; ficou em {Estado}")]
    private partial void RegistrarRecuperacao(Guid documento, int tentativas, EstadoDoDocumento estado);
}
