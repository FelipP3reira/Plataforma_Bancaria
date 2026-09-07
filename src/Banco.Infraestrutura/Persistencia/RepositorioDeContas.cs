using System.Data;
using System.Globalization;
using Banco.Aplicacao.Portas;
using Banco.Dominio.Contas;
using Banco.Dominio.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Banco.Infraestrutura.Persistencia;

public sealed class RepositorioDeContas : IRepositorioDeContas
{
    private readonly ContextoDoBanco contexto;

    public RepositorioDeContas(ContextoDoBanco contexto) => this.contexto = contexto;

    public Task<Conta?> PorId(Guid id, CancellationToken cancelamento) =>
        contexto.Contas.FirstOrDefaultAsync(conta => conta.Id == id, cancelamento);

    public Task<Conta?> PorNumero(NumeroDaConta numero, CancellationToken cancelamento) =>
        contexto.Contas.FirstOrDefaultAsync(conta => conta.Numero == numero, cancelamento);

    /// <remarks>
    /// O unico SQL escrito a mao do sistema, e por falta de alternativa: <c>UPDLOCK</c> e
    /// uma dica de tabela do SQL Server e nao existe no LINQ. O parametro entra pela
    /// interpolacao do <c>FromSql</c>, que o EF transforma em parametro de verdade — nao
    /// ha concatenacao de texto com valor vindo de fora.
    /// <para>
    /// <c>UPDLOCK</c> pega uma trava de atualizacao na leitura, e <c>ROWLOCK</c> pede que
    /// ela fique na linha em vez de escalar para a pagina — sem isso, travar uma conta
    /// poderia travar as vizinhas que por acaso moram na mesma pagina do indice.
    /// </para>
    /// <para>
    /// As colunas sao listadas uma a uma de proposito. Com <c>SELECT *</c>, uma coluna nova
    /// no modelo que ainda nao existisse na tabela passaria despercebida ate a primeira
    /// leitura em producao.
    /// </para>
    /// </remarks>
    public Task<Conta?> PorIdParaMovimentar(Guid id, CancellationToken cancelamento) =>
        contexto.Contas
            .FromSql(
                $"""
                 SELECT [Id], [Numero], [Titular], [Saldo], [UltimaSequencia], [AbertaEm], [AtualizadaEm]
                 FROM [Contas] WITH (UPDLOCK, ROWLOCK)
                 WHERE [Id] = {id}
                 """)
            .FirstOrDefaultAsync(cancelamento);

    public void Adicionar(Conta conta) => contexto.Contas.Add(conta);

    public void Adicionar(Lancamento lancamento) => contexto.Lancamentos.Add(lancamento);

    public Task<Lancamento?> PorChaveDeIdempotencia(
        Guid contaId,
        string chave,
        CancellationToken cancelamento) =>
        contexto.Lancamentos
            .AsNoTracking()
            .FirstOrDefaultAsync(
                linha => linha.ContaId == contaId && linha.ChaveIdempotencia == chave,
                cancelamento);

    /// <remarks>
    /// Comando cru em vez de <c>SqlQuery</c> por imposicao do SQL Server: <c>SqlQuery</c>
    /// embrulha o texto numa subconsulta para poder compor LINQ em cima, e
    /// <c>NEXT VALUE FOR</c> e proibido dentro de subconsulta. Nao ha valor vindo de fora
    /// neste comando — o nome da sequencia e uma constante do proprio contexto.
    /// <para>
    /// A transacao corrente e amarrada a mao: sem isso o comando roda fora dela e o numero
    /// sairia mesmo que a abertura da conta fosse desfeita depois. Sequencia nao volta
    /// atras, entao o buraco seria permanente.
    /// </para>
    /// </remarks>
    public async Task<NumeroDaConta> ProximoNumeroDeConta(CancellationToken cancelamento)
    {
        var conexao = contexto.Database.GetDbConnection();

        if (conexao.State != ConnectionState.Open)
        {
            await conexao.OpenAsync(cancelamento).ConfigureAwait(false);
        }

        await using var comando = conexao.CreateCommand();
        comando.CommandText = $"SELECT NEXT VALUE FOR [{ContextoDoBanco.SequenciaDeContas}]";
        comando.Transaction = contexto.Database.CurrentTransaction?.GetDbTransaction();

        var sequencia = await comando.ExecuteScalarAsync(cancelamento).ConfigureAwait(false);

        return NumeroDaConta.DaSequencia(Convert.ToInt64(sequencia, CultureInfo.InvariantCulture));
    }
}
