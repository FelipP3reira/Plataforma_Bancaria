using Banco.Dominio.Comum;

namespace Banco.Dominio.Ledger;

public enum TipoDeLancamento
{
    Credito = 1,
    Debito = 2,
}

/// <summary>
/// Uma linha do ledger. Imutavel: nasce e nunca muda.
/// </summary>
/// <remarks>
/// Nao existe caminho de alteracao nem de exclusao. Estorno e um lancamento novo em sentido
/// contrario, e nao a edicao do original — o que aconteceu continua tendo acontecido, e o
/// extrato conta a historia inteira em vez do resultado final.
/// </remarks>
/// <param name="Sequencia">
/// Posicao na conta, comecando em 1. Com indice unico em <c>(ContaId, Sequencia)</c>, ela e
/// a garantia estrutural do ledger: duas gravacoes concorrentes que lessem o mesmo saldo
/// tentariam gravar a mesma posicao, e o banco recusa a segunda. E a rede embaixo da trava.
/// </param>
/// <param name="SaldoDepois">
/// Saldo da conta logo apos este lancamento. Guardado, e nao recalculado: e o que torna a
/// reconciliacao uma verificacao de corrente — cada linha tem que bater com a anterior — em
/// vez de uma soma da tabela inteira, e o que deixa o extrato mostrar saldo corrente sem
/// somar nada.
/// </param>
public sealed class Lancamento
{
    public const int TamanhoMaximoDaDescricao = 140;

    private Lancamento()
    {
    }

    internal Lancamento(
        Guid contaId,
        long sequencia,
        TipoDeLancamento tipo,
        Dinheiro valor,
        Dinheiro saldoDepois,
        string descricao,
        string origem,
        string chaveIdempotencia,
        DateTimeOffset criadoEm,
        Guid? transferenciaId)
    {
        // GUID v7 e ordenado no tempo: como o SQL Server usa o indice agrupado da chave
        // primaria para ordenar as linhas em disco, GUID aleatorio espalharia a insercao
        // por todas as paginas — e ledger e uma tabela que so cresce por insercao.
        Id = Guid.CreateVersion7();
        ContaId = contaId;
        Sequencia = sequencia;
        Tipo = tipo;
        Valor = valor;
        SaldoDepois = saldoDepois;
        Descricao = descricao;
        Origem = origem;
        ChaveIdempotencia = chaveIdempotencia;
        CriadoEm = criadoEm;
        TransferenciaId = transferenciaId;
    }

    public Guid Id { get; private set; }

    public Guid ContaId { get; private set; }

    public long Sequencia { get; private set; }

    public TipoDeLancamento Tipo { get; private set; }

    /// <summary>Sempre positivo. O sentido do dinheiro esta em <see cref="Tipo"/>.</summary>
    public Dinheiro Valor { get; private set; }

    public Dinheiro SaldoDepois { get; private set; }

    public string Descricao { get; private set; } = string.Empty;

    /// <summary>
    /// Quem mandou fazer. Substitui a identidade autenticada enquanto ela nao existe.
    /// </summary>
    public string Origem { get; private set; } = string.Empty;

    /// <summary>
    /// A chave que o cliente mandou. Fica gravada, e nao so conferida: e por ela que um
    /// reenvio encontra o lancamento que ja existe em vez de criar um segundo.
    /// </summary>
    public string ChaveIdempotencia { get; private set; } = string.Empty;

    public DateTimeOffset CriadoEm { get; private set; }

    /// <summary>A transferencia de que este lancamento e perna, se for.</summary>
    public Guid? TransferenciaId { get; private set; }

    /// <summary>Quanto este lancamento move o saldo, com sinal.</summary>
    public decimal Efeito => EfeitoDe(Tipo, Valor.Valor);

    /// <summary>
    /// O sinal que o tipo da ao valor.
    /// </summary>
    /// <remarks>
    /// Estatico porque quem le o ledger sem montar a entidade — o extrato, por exemplo —
    /// precisa da mesma conta. Repetir o <c>if</c> do lado de fora seria deixar duas
    /// versoes da regra livres para divergirem, e a que divergisse seria a que inverte o
    /// sinal de um debito.
    /// </remarks>
    public static decimal EfeitoDe(TipoDeLancamento tipo, decimal valor) =>
        tipo == TipoDeLancamento.Credito ? valor : -valor;
}
