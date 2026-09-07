using Banco.Aplicacao.Extrato;
using Banco.Aplicacao.Portas;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infraestrutura.Persistencia;

/// <summary>
/// O extrato em uma consulta so.
/// </summary>
/// <remarks>
/// Sem rastreamento e sem navegacao carregada por linha: o extrato e leitura, e a
/// transferencia entra como id em vez de objeto para que ler cem linhas continue sendo uma
/// ida ao banco, e nao cento e uma.
/// </remarks>
public sealed class ExtratoDaConta : IExtratoDaConta
{
    private readonly ContextoDoBanco contexto;

    public ExtratoDaConta(ContextoDoBanco contexto) => this.contexto = contexto;

    public async Task<IReadOnlyList<LinhaDoExtrato>> Linhas(
        FiltroDoExtrato filtro,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var consulta = contexto.Lancamentos
            .AsNoTracking()
            .Where(linha =>
                linha.ContaId == filtro.ContaId
                && linha.CriadoEm >= filtro.De
                && linha.CriadoEm <= filtro.Ate);

        // O predicado do marcador, escrito para o indice: a comparacao principal e em
        // CriadoEm, que e a coluna ordenada logo depois de ContaId, e a sequencia so
        // aparece no empate. Invertido — sequencia primeiro — o banco perderia a busca e
        // varreria o periodo inteiro para achar onde a pagina anterior parou.
        if (filtro.Marcador is { } marcador)
        {
            var instante = marcador.CriadoEm;
            var sequencia = marcador.Sequencia;

            consulta = consulta.Where(linha =>
                linha.CriadoEm < instante
                || (linha.CriadoEm == instante && linha.Sequencia < sequencia));
        }

        // Colunas escolhidas a mao: a chave de idempotencia fica de fora do extrato, e
        // trazer coluna que ninguem le e trabalho pago em toda pagina.
        var linhas = await consulta
            .OrderByDescending(linha => linha.CriadoEm)
            .ThenByDescending(linha => linha.Sequencia)
            .Take(filtro.Tamanho + 1)
            .Select(linha => new
            {
                linha.Id,
                linha.Sequencia,
                linha.Tipo,
                linha.Valor,
                linha.SaldoDepois,
                linha.Descricao,
                linha.Origem,
                linha.CriadoEm,
                linha.TransferenciaId,
            })
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return
        [
            .. linhas.Select(linha => new LinhaDoExtrato(
                linha.Id,
                linha.Sequencia,
                linha.Tipo,
                linha.Valor.Valor,
                linha.SaldoDepois.Valor,
                linha.Descricao,
                linha.Origem,
                linha.CriadoEm,
                linha.TransferenciaId)),
        ];
    }
}
