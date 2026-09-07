using Banco.Dominio.Comum;
using Banco.Dominio.Contas;
using Banco.Dominio.Erros;
using Banco.Dominio.Ledger;

namespace Banco.Dominio.Transferencias;

/// <summary>
/// As duas pernas de uma transferencia, produzidas juntas.
/// </summary>
public sealed record TransferenciaRealizada(
    Transferencia Transferencia,
    Lancamento Debito,
    Lancamento Credito);

/// <summary>
/// Uma transferencia entre duas contas.
/// </summary>
/// <remarks>
/// Existe como registro proprio, e nao so como dois lancamentos parecidos, por duas razoes.
/// A primeira e a chave de idempotencia: ela e uma so para a operacao inteira, e precisa de
/// um lugar onde caiba uma vez — nao duas, uma em cada perna. A segunda e auditoria:
/// perguntar "o que aconteceu com esta transferencia" nao pode depender de adivinhar quais
/// dois lancamentos, em contas diferentes, formavam o par.
/// </remarks>
public sealed class Transferencia
{
    private Transferencia()
    {
    }

    private Transferencia(
        Guid id,
        Guid contaOrigemId,
        Guid contaDestinoId,
        Dinheiro valor,
        string descricao,
        string origem,
        string chaveIdempotencia,
        DateTimeOffset criadaEm)
    {
        Id = id;
        ContaOrigemId = contaOrigemId;
        ContaDestinoId = contaDestinoId;
        Valor = valor;
        Descricao = descricao;
        Origem = origem;
        ChaveIdempotencia = chaveIdempotencia;
        CriadaEm = criadaEm;
    }

    public Guid Id { get; private set; }

    public Guid ContaOrigemId { get; private set; }

    public Guid ContaDestinoId { get; private set; }

    public Dinheiro Valor { get; private set; }

    public string Descricao { get; private set; } = string.Empty;

    public string Origem { get; private set; } = string.Empty;

    /// <summary>A chave que o cliente mandou, valida dentro da conta de origem.</summary>
    public string ChaveIdempotencia { get; private set; } = string.Empty;

    public DateTimeOffset CriadaEm { get; private set; }

    /// <summary>
    /// Debita a origem e credita o destino, nesta ordem.
    /// </summary>
    /// <remarks>
    /// A ordem importa: o debito e o unico dos dois que pode ser recusado, e ele acontece
    /// primeiro. Creditar antes deixaria o destino com dinheiro por um instante, e o
    /// tratamento de "nao tinha saldo" viraria um desfazimento em vez de uma recusa.
    /// <para>
    /// Os dois lancamentos saem daqui juntos, de um metodo so, porque e isso que garante
    /// que uma transferencia seja sempre exatamente um debito e um credito do mesmo valor.
    /// Se a montagem morasse no caso de uso, nada impediria um caminho novo de creditar
    /// valor diferente do que debitou.
    /// </para>
    /// </remarks>
    /// <exception cref="SaldoInsuficienteException">Quando a origem nao cobre o valor.</exception>
    public static TransferenciaRealizada Entre(
        Conta origem,
        Conta destino,
        PedidoDeLancamento pedido)
    {
        ArgumentNullException.ThrowIfNull(origem);
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(pedido);

        if (origem.Id == destino.Id)
        {
            throw new ContaInvalidaException("Origem e destino sao a mesma conta.");
        }

        var id = Guid.CreateVersion7();

        // As pernas nao carregam a chave do cliente, e sim uma derivada do id da
        // transferencia. A chave do cliente vale por conta, e a perna de credito cai numa
        // conta que nunca a viu — se ela fosse gravada la, uma operacao qualquer do dono do
        // destino que por acaso usasse a mesma chave colidiria com uma transferencia que
        // nao tem nada a ver com ele.
        var doDebito = pedido with
        {
            ChaveIdempotencia = ChaveDaPerna(id, "debito"),
            TransferenciaId = id,
        };

        var doCredito = pedido with
        {
            ChaveIdempotencia = ChaveDaPerna(id, "credito"),
            TransferenciaId = id,
        };

        var debito = origem.Debitar(doDebito);
        var credito = destino.Creditar(doCredito);

        return new TransferenciaRealizada(
            new Transferencia(
                id,
                origem.Id,
                destino.Id,
                pedido.Valor,
                pedido.Descricao.Trim(),
                pedido.Origem.Trim(),
                pedido.ChaveIdempotencia,
                pedido.Agora),
            debito,
            credito);
    }

    private static string ChaveDaPerna(Guid transferenciaId, string perna) =>
        $"transferencia:{transferenciaId:N}:{perna}";
}
