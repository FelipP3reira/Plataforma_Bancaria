using System.Globalization;
using Banco.Aplicacao.Contas;
using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Portas;
using Banco.Dominio.Documentos;
using Banco.Dominio.Documentos.Boletos;
using Banco.Dominio.Erros;

namespace Banco.Aplicacao.Documentos;

/// <param name="Novo">
/// Falso quando este documento ja tinha sido pago. A resposta descreve o debito que existe, e
/// nenhum segundo debito foi criado.
/// </param>
public sealed record PagamentoDoDocumento(
    Guid DocumentoId,
    Guid ContaId,
    Guid LancamentoId,
    decimal Valor,
    string LinhaDigitavel,
    EstadoDoDocumento Estado,
    bool Novo);

/// <summary>
/// Paga o boleto de um documento conferido, debitando a conta dele.
/// </summary>
/// <remarks>
/// A chave de idempotencia e <b>derivada do documento</b>, e nao sorteada nem pedida a quem chama.
/// Duas consequencias praticas. Repetir o pedido depois de uma falha de rede nao cobra duas vezes,
/// porque a segunda tentativa apresenta a mesma chave e a conta reconhece o debito que ja existe. E
/// o mesmo boleto enviado de novo como arquivo tambem nao cobra duas vezes, porque o upload
/// reconhece o conteudo pelo hash e devolve o mesmo documento — a protecao vale contra o erro de
/// quem integra <em>e</em> contra o erro de quem usa.
/// </remarks>
public sealed class PagarDocumento
{
    private readonly IRepositorioDeDocumentos documentos;
    private readonly MovimentarConta movimentar;
    private readonly IUnidadeDeTrabalho unidade;
    private readonly TimeProvider relogio;

    public PagarDocumento(
        IRepositorioDeDocumentos documentos,
        MovimentarConta movimentar,
        IUnidadeDeTrabalho unidade,
        TimeProvider relogio)
    {
        this.documentos = documentos;
        this.movimentar = movimentar;
        this.unidade = unidade;
        this.relogio = relogio;
    }

    public async Task<PagamentoDoDocumento> Executar(Guid id, string operador, CancellationToken cancelamento)
    {
        var documento = await documentos.PorId(id, cancelamento).ConfigureAwait(false)
            ?? throw new DocumentoNaoEncontradoException($"Documento {id} nao encontrado.");

        // Ja pago devolve o que existe em vez de recusar. Quem repetiu o pedido por causa de uma
        // resposta perdida quer saber o resultado, e 409 nesse caso manda investigar um problema
        // que nao houve.
        if (documento.Estado == EstadoDoDocumento.Pago)
        {
            return Resposta(documento, documento.LancamentoDoPagamentoId!.Value, Valor(documento), novo: false);
        }

        // Antes de qualquer coisa que mova dinheiro: o documento pode ser pago? Documento
        // esperando revisao respondia nao — mas so na hora de marcar, depois de a conta ja ter
        // sido debitada. O dinheiro saia e o documento ficava na fila.
        documento.GarantirQuePodeSerPago();

        var linha = documento.Campo(NomeDoCampo.LinhaDigitavel).ValorFinal;
        var valor = Valor(documento);

        // O debito vem antes de marcar como pago. Na ordem inversa, um debito recusado por saldo
        // deixaria o documento dizendo que foi pago sem dinheiro nenhum ter saido.
        //
        // As duas gravacoes nao cabem numa transacao so porque a movimentacao abre e confirma a
        // dela — e isso e proposital: e ela que segura a trava da conta, e manter essa trava
        // enquanto o documento e atualizado a estenderia sem motivo. O que fecha a brecha e a
        // chave derivada: processo que morra entre o debito e a marcacao deixa o documento em
        // Extraido, e o pedido repetido reencontra o mesmo lancamento e conclui a marcacao.
        var lancamento = await movimentar
            .Sacar(
                new PedidoDeMovimentacao(
                    documento.ContaId,
                    valor,
                    $"Pagamento de boleto {linha}",
                    operador,
                    documento.ChaveDePagamento()),
                cancelamento)
            .ConfigureAwait(false);

        documento.Pagar(lancamento.Lancamento.Id, relogio.GetUtcNow());

        await unidade.Salvar(cancelamento).ConfigureAwait(false);

        return Resposta(documento, lancamento.Lancamento.Id, valor, lancamento.Novo);
    }

    /// <remarks>
    /// Le o valor <em>final</em> do campo, que e o corrigido quando alguem corrigiu. Ler o valor
    /// que a maquina extraiu cobraria o numero errado justamente nos documentos que passaram pela
    /// revisao — os que mais precisavam de cuidado.
    /// </remarks>
    private static decimal Valor(Documento documento)
    {
        var texto = documento.Campo(NomeDoCampo.Valor).ValorFinal;

        if (!decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor)
            || valor <= 0m)
        {
            // Boleto de valor a combinar existe e e legitimo; pagar sozinho um valor que ninguem
            // definiu nao e. Quem sabe quanto pagar corrige o campo na revisao.
            throw new BoletoInvalidoException(
                "O documento nao tem valor a pagar. Corrija o campo Valor na revisao.");
        }

        return valor;
    }

    private static PagamentoDoDocumento Resposta(
        Documento documento,
        Guid lancamentoId,
        decimal valor,
        bool novo) =>
        new(
            documento.Id,
            documento.ContaId,
            lancamentoId,
            valor,
            documento.Campo(NomeDoCampo.LinhaDigitavel).ValorFinal,
            documento.Estado,
            novo);
}
