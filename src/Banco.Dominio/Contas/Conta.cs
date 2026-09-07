using Banco.Dominio.Comum;
using Banco.Dominio.Erros;
using Banco.Dominio.Ledger;

namespace Banco.Dominio.Contas;

/// <summary>
/// Raiz do agregado. Produz lancamentos e mantem o saldo materializado em passo com eles.
/// </summary>
/// <remarks>
/// O agregado NAO carrega o ledger. Uma conta com dez mil lancamentos nao pode ler dez mil
/// linhas para gravar a decima mil e uma — o custo de um deposito passaria a depender de
/// quantos depositos ja houve. O que a conta guarda e o resultado: <see cref="Saldo"/> e
/// <see cref="UltimaSequencia"/>, os dois campos de que ela precisa para produzir o proximo
/// lancamento em tempo constante.
/// <para>
/// Isso significa que o saldo e um cache, e cache mente quando ninguem confere. A conferencia
/// existe e e explicita: o ledger continua sendo a verdade, e a reconciliacao soma as linhas
/// e compara com este campo.
/// </para>
/// </remarks>
public sealed class Conta
{
    public const int TamanhoMaximoDoTitular = 150;

    private Conta()
    {
    }

    private Conta(NumeroDaConta numero, string titular, DateTimeOffset agora)
    {
        Id = Guid.CreateVersion7();
        Numero = numero;
        Titular = titular.Trim();
        Saldo = Dinheiro.Zero;
        UltimaSequencia = 0;
        AbertaEm = agora;
        AtualizadaEm = agora;
    }

    public Guid Id { get; private set; }

    public NumeroDaConta Numero { get; private set; } = null!;

    public string Titular { get; private set; } = string.Empty;

    /// <summary>Saldo materializado. Reconciliavel contra o ledger, nunca substituto dele.</summary>
    public Dinheiro Saldo { get; private set; }

    /// <summary>Sequencia do ultimo lancamento gravado nesta conta.</summary>
    public long UltimaSequencia { get; private set; }

    public DateTimeOffset AbertaEm { get; private set; }

    public DateTimeOffset AtualizadaEm { get; private set; }

    public static Conta Abrir(NumeroDaConta numero, string titular, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(numero);

        if (string.IsNullOrWhiteSpace(titular))
        {
            throw new ContaInvalidaException("Titular obrigatorio.");
        }

        if (titular.Trim().Length > TamanhoMaximoDoTitular)
        {
            throw new ContaInvalidaException($"Titular passa de {TamanhoMaximoDoTitular} caracteres.");
        }

        // Conta nasce zerada e sem lancamento nenhum. Um "deposito inicial" embutido na
        // abertura seria dinheiro entrando sem linha no ledger que o explique.
        return new Conta(numero, titular, agora);
    }

    public Lancamento Creditar(PedidoDeLancamento pedido) => Registrar(TipoDeLancamento.Credito, pedido);

    /// <exception cref="SaldoInsuficienteException">
    /// Quando o valor passa do saldo. Nao ha cheque especial: a conferencia acontece aqui,
    /// no agregado, e nao so na borda — assim nenhum caso de uso futuro consegue debitar
    /// mais do que existe por ter esquecido de checar.
    /// </exception>
    public Lancamento Debitar(PedidoDeLancamento pedido)
    {
        ArgumentNullException.ThrowIfNull(pedido);

        if (pedido.Valor > Saldo)
        {
            throw new SaldoInsuficienteException(
                $"Saldo de {Saldo} nao cobre o debito de {pedido.Valor}.");
        }

        return Registrar(TipoDeLancamento.Debito, pedido);
    }

    private Lancamento Registrar(TipoDeLancamento tipo, PedidoDeLancamento pedido)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        pedido.Validar();

        var saldoDepois = tipo == TipoDeLancamento.Credito
            ? Saldo + pedido.Valor
            : Saldo - pedido.Valor;

        var lancamento = new Lancamento(
            Id,
            UltimaSequencia + 1,
            tipo,
            pedido.Valor,
            saldoDepois,
            pedido.Descricao.Trim(),
            pedido.Origem.Trim(),
            pedido.ChaveIdempotencia,
            pedido.Agora);

        // Saldo e sequencia so avancam depois que o lancamento existe. Se algo acima
        // lancasse, a conta ficaria com saldo que nenhuma linha do ledger justifica.
        Saldo = saldoDepois;
        UltimaSequencia = lancamento.Sequencia;
        AtualizadaEm = pedido.Agora;

        return lancamento;
    }
}
