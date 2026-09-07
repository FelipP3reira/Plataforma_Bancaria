namespace Banco.Aplicacao.Conciliacao;

/// <summary>
/// O resultado de conferir o saldo materializado contra o ledger.
/// </summary>
/// <remarks>
/// O saldo na linha da conta e um cache, e cache mente quando ninguem confere. Isto e a
/// conferencia — e ela existe justamente porque a alternativa honesta, somar o ledger a
/// cada movimentacao, faria o custo de um saque depender de quantos saques ja houve.
/// </remarks>
/// <param name="SomaDoLedger">Soma de todos os lancamentos, com sinal.</param>
/// <param name="QuebrasNaCorrente">
/// Linhas onde o <c>SaldoDepois</c> nao bate com o da anterior mais o proprio efeito, ou
/// onde a sequencia pulou. Zero e o unico valor aceitavel.
/// </param>
public sealed record ConciliacaoDaConta(
    Guid ContaId,
    string Numero,
    decimal SaldoMaterializado,
    decimal SomaDoLedger,
    long UltimaSequenciaDaConta,
    long MaiorSequenciaNoLedger,
    long Lancamentos,
    long QuebrasNaCorrente)
{
    public decimal Diferenca => SaldoMaterializado - SomaDoLedger;

    /// <summary>
    /// As quatro coisas que precisam ser verdade ao mesmo tempo.
    /// </summary>
    /// <remarks>
    /// Saldo batendo com a soma nao basta: um lancamento a mais e outro a menos que se
    /// anulem dariam a mesma soma. Por isso a contagem, a maior sequencia e a corrente
    /// entram junto — cada uma pega um jeito diferente de o ledger estar errado.
    /// </remarks>
    public bool Bate =>
        SaldoMaterializado == SomaDoLedger
        && UltimaSequenciaDaConta == MaiorSequenciaNoLedger
        && Lancamentos == MaiorSequenciaNoLedger
        && QuebrasNaCorrente == 0;
}
