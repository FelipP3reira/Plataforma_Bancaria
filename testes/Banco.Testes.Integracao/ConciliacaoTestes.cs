using Banco.Infraestrutura.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Banco.Testes.Integracao;

/// <summary>
/// Prova que a conferencia entre o saldo materializado e o ledger detecta divergencia.
/// </summary>
/// <remarks>
/// Os testes estragam o banco de proposito, por fora da API. Conferencia que so foi testada
/// com dados corretos nao prova nada: ela precisa acusar quando algo esta errado, e a unica
/// forma de saber isso e produzir o erro.
/// </remarks>
[Collection(ColecaoDaApi.Nome)]
public class ConciliacaoTestes
{
    private readonly FabricaDaApi fabrica;

    public ConciliacaoTestes(FabricaDaApi fabrica) => this.fabrica = fabrica;

    private async Task ExecutarSql(string sql)
    {
        using var escopo = fabrica.Services.CreateScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<ContextoDoBanco>();

        await contexto.Database.ExecuteSqlRawAsync(sql);
    }

    [Fact]
    public async Task ContaSemMovimentacaoBate()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();

        var conferencia = await cliente.Conciliacao(conta.Id);

        Assert.True(conferencia!.Bate);
        Assert.Equal(0m, conferencia.SaldoMaterializado);
        Assert.Equal(0m, conferencia.SomaDoLedger);
        Assert.Equal(0, conferencia.Lancamentos);
        Assert.Equal(0, conferencia.QuebrasNaCorrente);
    }

    [Fact]
    public async Task ContaMovimentadaBateEmTodosOsQuatroCriterios()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();

        await cliente.Depositar(conta.Id, 1_000m);
        await cliente.Sacar(conta.Id, 249.37m);
        await cliente.Depositar(conta.Id, 15.63m);
        await cliente.Sacar(conta.Id, 766.26m);

        var conferencia = await cliente.Conciliacao(conta.Id);

        Assert.True(conferencia!.Bate);
        Assert.Equal(0m, conferencia.Diferenca);
        Assert.Equal(4, conferencia.Lancamentos);
        Assert.Equal(4, conferencia.MaiorSequenciaNoLedger);
        Assert.Equal(4, conferencia.UltimaSequenciaDaConta);
        Assert.Equal(0, conferencia.QuebrasNaCorrente);
    }

    /// <summary>
    /// Alguem mexeu no saldo por fora — o caso que o saldo materializado existe para
    /// arriscar, e que a conferencia existe para pegar.
    /// </summary>
    [Fact]
    public async Task SaldoAdulteradoPorForaEhDetectado()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();
        await cliente.Depositar(conta.Id, 500m);

        await ExecutarSql($"UPDATE [Contas] SET [Saldo] = 999.99 WHERE [Id] = '{conta.Id}'");

        var conferencia = await cliente.Conciliacao(conta.Id);

        Assert.False(conferencia!.Bate);
        Assert.Equal(999.99m, conferencia.SaldoMaterializado);
        Assert.Equal(500m, conferencia.SomaDoLedger);
        Assert.Equal(499.99m, conferencia.Diferenca);
    }

    /// <summary>
    /// Lancamento apagado do ledger: a soma muda, e a contagem deixa de bater com a maior
    /// sequencia — dois criterios diferentes acusando o mesmo estrago.
    /// </summary>
    [Fact]
    public async Task LancamentoApagadoDoLedgerEhDetectado()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();
        await cliente.Depositar(conta.Id, 100m);
        await cliente.Depositar(conta.Id, 200m);
        await cliente.Depositar(conta.Id, 300m);

        await ExecutarSql($"DELETE FROM [Lancamentos] WHERE [ContaId] = '{conta.Id}' AND [Sequencia] = 2");

        var conferencia = await cliente.Conciliacao(conta.Id);

        Assert.False(conferencia!.Bate);
        Assert.Equal(600m, conferencia.SaldoMaterializado);
        Assert.Equal(400m, conferencia.SomaDoLedger);
        Assert.Equal(2, conferencia.Lancamentos);
        Assert.Equal(3, conferencia.MaiorSequenciaNoLedger);
        Assert.True(conferencia.QuebrasNaCorrente > 0);
    }

    /// <summary>
    /// O caso que a soma sozinha nao pegaria: dois lancamentos adulterados que se anulam.
    /// </summary>
    /// <remarks>
    /// Trocar um credito de cem por um de duzentos e um debito de cem por um de duzentos
    /// deixa a soma exatamente igual. So a verificacao da corrente — cada SaldoDepois
    /// contra o anterior — enxerga que as linhas nao contam mais a mesma historia.
    /// </remarks>
    [Fact]
    public async Task DoisLancamentosAdulteradosQueSeAnulamAindaAssimSaoDetectados()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();
        await cliente.Depositar(conta.Id, 100m);
        await cliente.Sacar(conta.Id, 100m);

        await ExecutarSql(
            $"UPDATE [Lancamentos] SET [Valor] = 200 WHERE [ContaId] = '{conta.Id}'");

        var conferencia = await cliente.Conciliacao(conta.Id);

        // A soma continua zero, e o saldo tambem: os dois criterios de valor concordam.
        Assert.Equal(conferencia!.SaldoMaterializado, conferencia.SomaDoLedger);
        Assert.Equal(2, conferencia.Lancamentos);

        // E ainda assim nao bate, porque a corrente quebrou.
        Assert.True(conferencia.QuebrasNaCorrente > 0);
        Assert.False(conferencia.Bate);
    }

    [Fact]
    public async Task ConciliacaoDeContaInexistenteDevolveNaoEncontrada()
    {
        var resposta = await fabrica.CreateClient().GetAsync($"/contas/{Guid.NewGuid()}/conciliacao");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, resposta.StatusCode);
    }

    /// <summary>
    /// Depois de muita movimentacao concorrente, o ledger continua integro. E o teste que
    /// amarra a trava a consistencia: se a concorrencia furasse o controle em algum lugar,
    /// a conferencia acusaria aqui.
    /// </summary>
    [Fact]
    public async Task LedgerContinuaIntegroDepoisDeMovimentacaoConcorrente()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(1_000m);

        var partida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operacoes = Enumerable.Range(0, 30).Select(async indice =>
        {
            await partida.Task;
            return indice % 2 == 0
                ? await cliente.Depositar(conta, 13.37m)
                : await cliente.Sacar(conta, 11.11m);
        }).ToArray();

        partida.SetResult();
        await Task.WhenAll(operacoes);

        var conferencia = await cliente.Conciliacao(conta);

        Assert.True(conferencia!.Bate, $"diferenca de {conferencia.Diferenca}");
        Assert.Equal(0, conferencia.QuebrasNaCorrente);
    }
}
