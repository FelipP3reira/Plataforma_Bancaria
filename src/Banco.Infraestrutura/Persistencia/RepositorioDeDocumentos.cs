using Banco.Aplicacao.Portas;
using Banco.Dominio.Documentos;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infraestrutura.Persistencia;

public sealed class RepositorioDeDocumentos : IRepositorioDeDocumentos
{
    /// <remarks>
    /// Listadas uma a uma, e nao <c>SELECT *</c>: coluna nova no modelo que ainda nao exista
    /// na tabela quebra aqui, na primeira leitura da fila, e nao no meio de uma extracao.
    /// </remarks>
    private const string Colunas = """
        [Id], [ContaId], [NomeOriginal], [Tipo], [TamanhoEmBytes], [Hash], [CaminhoRelativo],
        [Origem], [Estado], [RecebidoEm], [AtualizadoEm], [LeaseAte], [Tentativas],
        [UltimoErro], [ConteudoExtraido], [Confianca], [ConfiancaDoTexto], [ExtraidoEm],
        [LancamentoDoPagamentoId], [PagoEm]
        """;

    /// <remarks>
    /// <c>UPDLOCK</c> pega a trava de atualizacao ja na leitura, para que o proprio SELECT
    /// que escolhe o documento seja o que o reserva.
    /// <para>
    /// <c>READPAST</c> e o que faz a fila ser fila: em vez de esperar pela linha que outro
    /// worker travou, este SELECT a pula e pega a seguinte. Sem ele, dois workers nao
    /// processariam dois documentos em paralelo — o segundo ficaria bloqueado atras do
    /// primeiro, e mais processo nao daria vazao nenhuma a mais.
    /// </para>
    /// <para>
    /// A ordem desempata pelo <c>Id</c> porque dois uploads podem gravar o mesmo
    /// <c>RecebidoEm</c>. Sem desempate, a ordem da fila ficaria indefinida.
    /// </para>
    /// </remarks>
    private const string SqlDaFila = $$"""
        SELECT TOP (1) {{Colunas}}
        FROM [Documentos] WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE [Estado] = {0}
        ORDER BY [RecebidoEm], [Id]
        """;

    private const string SqlDosVencidos = $$"""
        SELECT TOP ({0}) {{Colunas}}
        FROM [Documentos] WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE [Estado] = {1} AND [LeaseAte] <= {2}
        ORDER BY [LeaseAte]
        """;

    private readonly ContextoDoBanco contexto;

    public RepositorioDeDocumentos(ContextoDoBanco contexto) => this.contexto = contexto;

    public Task<Documento?> PorId(Guid id, CancellationToken cancelamento) =>
        contexto.Documentos.FirstOrDefaultAsync(documento => documento.Id == id, cancelamento);

    public Task<Documento?> PorHash(Guid contaId, HashDoArquivo hash, CancellationToken cancelamento) =>
        contexto.Documentos.FirstOrDefaultAsync(
            documento => documento.ContaId == contaId && documento.Hash == hash,
            cancelamento);

    public void Adicionar(Documento documento) => contexto.Documentos.Add(documento);

    /// <remarks>
    /// <c>FromSqlRaw</c> e nao <c>FromSql</c> por causa da lista de colunas: a versao
    /// interpolada transforma todo buraco em parametro, e nome de coluna nao pode ser
    /// parametro. O texto e montado so de constantes desta classe; os valores continuam
    /// entrando como parametro pelos marcadores numerados.
    /// </remarks>
    public Task<Documento?> ProximoDaFila(CancellationToken cancelamento) =>
        contexto.Documentos
            .FromSqlRaw(SqlDaFila, (int)EstadoDoDocumento.Recebido)
            .FirstOrDefaultAsync(cancelamento);

    /// <remarks>
    /// LINQ comum e nao SQL a mao: esta consulta nao reserva nada e nao precisa de dica de tabela.
    /// O desempate pelo instante de chegada existe porque varios documentos empatam em confianca —
    /// tres boletos com o valor divergente marcam 0,20 os tres.
    /// </remarks>
    public async Task<IReadOnlyList<Documento>> ParaRevisao(int quantidade, CancellationToken cancelamento) =>
        await contexto.Documentos
            .AsNoTracking()
            .Where(documento => documento.Estado == EstadoDoDocumento.RequerRevisao)
            .OrderBy(documento => documento.Confianca)
            .ThenBy(documento => documento.RecebidoEm)
            .Take(quantidade)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Documento>> ComLeaseVencido(
        DateTimeOffset agora,
        int quantidade,
        CancellationToken cancelamento) =>
        await contexto.Documentos
            .FromSqlRaw(SqlDosVencidos, quantidade, (int)EstadoDoDocumento.Extraindo, agora)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);
}
