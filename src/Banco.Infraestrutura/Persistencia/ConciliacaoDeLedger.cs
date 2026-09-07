using Banco.Aplicacao.Conciliacao;
using Banco.Aplicacao.Portas;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infraestrutura.Persistencia;

/// <summary>
/// Linha crua da conferencia. Sem chave: e resultado de consulta, nao tabela.
/// </summary>
public sealed class LinhaDaConciliacao
{
    public Guid ContaId { get; init; }

    public string Numero { get; init; } = string.Empty;

    public decimal SaldoMaterializado { get; init; }

    public decimal SomaDoLedger { get; init; }

    public long UltimaSequenciaDaConta { get; init; }

    public long MaiorSequenciaNoLedger { get; init; }

    public long Lancamentos { get; init; }

    public long QuebrasNaCorrente { get; init; }
}

public sealed class ConciliacaoDeLedger : IConciliacaoDeLedger
{
    private readonly ContextoDoBanco contexto;

    public ConciliacaoDeLedger(ContextoDoBanco contexto) => this.contexto = contexto;

    /// <remarks>
    /// Uma consulta so, agregando no banco. As duas subconsultas fazem coisas diferentes:
    /// a primeira soma e conta — pega saldo divergente e lancamento faltando —, e a segunda
    /// usa <c>LAG</c> para andar pela corrente, comparando cada linha com a anterior. Sem a
    /// segunda, um lancamento a mais e outro a menos que se anulassem passariam batido,
    /// porque a soma continuaria certa.
    /// </remarks>
    public Task<ConciliacaoDaConta?> DaConta(Guid contaId, CancellationToken cancelamento) =>
        contexto.Set<LinhaDaConciliacao>()
            .FromSql(
                $"""
                 SELECT
                     c.[Id] AS [ContaId],
                     c.[Numero] AS [Numero],
                     c.[Saldo] AS [SaldoMaterializado],
                     ISNULL(t.[Soma], 0) AS [SomaDoLedger],
                     c.[UltimaSequencia] AS [UltimaSequenciaDaConta],
                     ISNULL(t.[MaiorSequencia], 0) AS [MaiorSequenciaNoLedger],
                     ISNULL(t.[Quantidade], 0) AS [Lancamentos],
                     ISNULL(q.[Quebras], 0) AS [QuebrasNaCorrente]
                 FROM [Contas] AS c
                 OUTER APPLY (
                     SELECT
                         SUM(CASE WHEN l.[Tipo] = 1 THEN l.[Valor] ELSE -l.[Valor] END) AS [Soma],
                         MAX(l.[Sequencia]) AS [MaiorSequencia],
                         COUNT_BIG(*) AS [Quantidade]
                     FROM [Lancamentos] AS l
                     WHERE l.[ContaId] = c.[Id]
                 ) AS t
                 OUTER APPLY (
                     SELECT COUNT_BIG(*) AS [Quebras]
                     FROM (
                         SELECT
                             l.[Sequencia],
                             l.[SaldoDepois],
                             CASE WHEN l.[Tipo] = 1 THEN l.[Valor] ELSE -l.[Valor] END AS [Efeito],
                             LAG(l.[Sequencia], 1, 0) OVER (ORDER BY l.[Sequencia]) AS [SequenciaAnterior],
                             LAG(l.[SaldoDepois], 1, 0) OVER (ORDER BY l.[Sequencia]) AS [SaldoAnterior]
                         FROM [Lancamentos] AS l
                         WHERE l.[ContaId] = c.[Id]
                     ) AS corrente
                     WHERE corrente.[SaldoDepois] <> corrente.[SaldoAnterior] + corrente.[Efeito]
                        OR corrente.[Sequencia] <> corrente.[SequenciaAnterior] + 1
                 ) AS q
                 WHERE c.[Id] = {contaId}
                 """)
            .AsNoTracking()
            .Select(linha => new ConciliacaoDaConta(
                linha.ContaId,
                linha.Numero,
                linha.SaldoMaterializado,
                linha.SomaDoLedger,
                linha.UltimaSequenciaDaConta,
                linha.MaiorSequenciaNoLedger,
                linha.Lancamentos,
                linha.QuebrasNaCorrente))
            .FirstOrDefaultAsync(cancelamento);
}
