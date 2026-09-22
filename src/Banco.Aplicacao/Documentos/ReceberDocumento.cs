using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Portas;
using Banco.Dominio.Documentos;
using Banco.Dominio.Erros;

namespace Banco.Aplicacao.Documentos;

public sealed record PedidoDeDocumento(
    Guid ContaId,
    string NomeOriginal,
    byte[] Conteudo,
    string Origem);

/// <param name="Novo">
/// Falso quando o mesmo arquivo ja tinha sido enviado nesta conta. A resposta e o documento
/// que ja existe.
/// </param>
public sealed record DocumentoRecebido(
    Guid Id,
    Guid ContaId,
    string NomeOriginal,
    TipoDeArquivo Tipo,
    long TamanhoEmBytes,
    string Hash,
    EstadoDoDocumento Estado,
    DateTimeOffset RecebidoEm,
    bool Novo);

/// <summary>
/// Recebe o arquivo, confere o que ele e de verdade e o poe na fila.
/// </summary>
/// <remarks>
/// Nao chama OCR nem IA: enfileira e devolve. Extracao passa por servico de terceiro e leva
/// segundos — prender a requisicao do cliente ate ela terminar entregaria tempo esgotado
/// nos arquivos grandes, que sao justamente os que mais demoram.
/// </remarks>
public sealed class ReceberDocumento
{
    private readonly IRepositorioDeDocumentos documentos;
    private readonly IArmazenamentoDeDocumentos armazenamento;
    private readonly IRepositorioDeContas contas;
    private readonly IUnidadeDeTrabalho unidade;
    private readonly TimeProvider relogio;

    public ReceberDocumento(
        IRepositorioDeDocumentos documentos,
        IArmazenamentoDeDocumentos armazenamento,
        IRepositorioDeContas contas,
        IUnidadeDeTrabalho unidade,
        TimeProvider relogio)
    {
        this.documentos = documentos;
        this.armazenamento = armazenamento;
        this.contas = contas;
        this.unidade = unidade;
        this.relogio = relogio;
    }

    public async Task<DocumentoRecebido> Executar(PedidoDeDocumento pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);

        _ = await contas.PorId(pedido.ContaId, cancelamento).ConfigureAwait(false)
            ?? throw new ContaNaoEncontradaException($"Conta {pedido.ContaId} nao encontrada.");

        // O tipo sai dos bytes antes de qualquer outra coisa. Arquivo que nao e PDF, PNG
        // nem JPEG nao chega a ocupar disco nem a virar linha no banco.
        if (pedido.Conteudo.Length < ReconhecedorDeArquivo.BytesNecessarios)
        {
            throw new ArquivoRecusadoException("Arquivo pequeno demais para ter tipo reconhecivel.");
        }

        var tipo = ReconhecedorDeArquivo.Reconhecer(
            pedido.Conteudo.AsSpan(0, ReconhecedorDeArquivo.BytesNecessarios));

        var hash = HashDoArquivo.De(pedido.Conteudo);

        // Reenvio do mesmo arquivo devolve o que ja existe, sem gravar de novo e sem
        // enfileirar uma segunda extracao. O indice unico em (ContaId, Hash) e a rede
        // embaixo desta conferencia, para o caso de dois uploads simultaneos.
        if (await documentos.PorHash(pedido.ContaId, hash, cancelamento).ConfigureAwait(false) is { } existente)
        {
            return Resposta(existente, novo: false);
        }

        await using var conteudo = new MemoryStream(pedido.Conteudo, writable: false);
        var caminho = await armazenamento
            .Guardar(hash, tipo, conteudo, cancelamento)
            .ConfigureAwait(false);

        var documento = Documento.Receber(
            pedido.ContaId,
            pedido.NomeOriginal,
            tipo,
            pedido.Conteudo.Length,
            hash,
            caminho,
            pedido.Origem,
            relogio.GetUtcNow());

        documentos.Adicionar(documento);
        await unidade.Salvar(cancelamento).ConfigureAwait(false);

        return Resposta(documento, novo: true);
    }

    private static DocumentoRecebido Resposta(Documento documento, bool novo) =>
        new(
            documento.Id,
            documento.ContaId,
            documento.NomeOriginal,
            documento.Tipo,
            documento.TamanhoEmBytes,
            documento.Hash.Texto,
            documento.Estado,
            documento.RecebidoEm,
            novo);
}
