using Banco.Dominio.Ledger;

namespace Banco.Aplicacao.Extrato;

/// <param name="SaldoDepois">
/// Saldo da conta logo apos esta linha. Vem gravado do ledger, e nao de uma soma corrida
/// aqui: e o que deixa o extrato mostrar saldo corrente sem depender de o cliente ter
/// pedido as paginas anteriores.
/// </param>
/// <param name="TransferenciaId">
/// A transferencia de que esta linha e perna, quando for. Deixa o cliente ligar as duas
/// pontas sem ter que casar valor e horario no olho.
/// </param>
public sealed record LinhaDoExtrato(
    Guid Id,
    long Sequencia,
    TipoDeLancamento Tipo,
    decimal Valor,
    decimal SaldoDepois,
    string Descricao,
    string Origem,
    DateTimeOffset CriadoEm,
    Guid? TransferenciaId)
{
    /// <summary>
    /// Quanto a linha moveu o saldo, com sinal.
    /// </summary>
    /// <remarks>
    /// Vai junto do tipo porque quem consome extrato costuma querer somar um periodo, e
    /// somar exige o sinal que <see cref="Valor"/> nao tem.
    /// </remarks>
    public decimal Efeito => Lancamento.EfeitoDe(Tipo, Valor);
}

/// <param name="ProximaPagina">
/// Marcador da proxima pagina, ou nulo quando acabou.
/// </param>
/// <remarks>
/// Sem total de linhas de proposito. Contar o periodo inteiro e justamente a consulta que
/// fica cara quando a conta tem milhares de lancamentos — e seria paga em toda pagina, para
/// mostrar um numero que muda entre uma pagina e outra. O cliente sabe que acabou quando
/// <see cref="ProximaPagina"/> vem nulo.
/// </remarks>
public sealed record PaginaDoExtrato(
    Guid ContaId,
    string Numero,
    DateTimeOffset De,
    DateTimeOffset Ate,
    IReadOnlyList<LinhaDoExtrato> Linhas,
    string? ProximaPagina);
