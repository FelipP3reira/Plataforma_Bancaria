using System.Net;

namespace Banco.Testes.Integracao;

/// <summary>
/// Transferencias cruzadas ao mesmo tempo: A para B e B para A.
/// </summary>
/// <remarks>
/// E o caso que a ordem de aquisicao de trava existe para evitar. Sem ela, uma operacao
/// trava A e pede B enquanto a outra trava B e pede A — cada uma esperando a trava que a
/// outra ja tem. O SQL Server detecta e mata uma como vitima de deadlock, o que vira 500
/// numa operacao que estava perfeitamente correta.
/// <para>
/// Com a ordem pelo id, as duas pegam as travas na mesma sequencia: quem chega depois
/// espera na primeira, e nao no meio.
/// </para>
/// </remarks>
[Collection(ColecaoDaApi.Nome)]
public class DeadlockTestes
{
    private readonly FabricaDaApi fabrica;

    public DeadlockTestes(FabricaDaApi fabrica) => this.fabrica = fabrica;

    [Fact]
    public async Task TransferenciasCruzadasAoMesmoTempoNaoTravamUmaAOutra()
    {
        var cliente = fabrica.CreateClient();
        var contaA = await cliente.ContaCom(1_000m);
        var contaB = await cliente.ContaCom(1_000m);

        var partida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Vinte pares cruzados: um par so raramente pega a janela em que o deadlock
        // aconteceria, e um teste que so as vezes reprova nao serve de rede.
        var cruzadas = Enumerable.Range(0, 20).SelectMany(_ => new[]
        {
            Disparar(cliente, partida, contaA, contaB),
            Disparar(cliente, partida, contaB, contaA),
        }).ToArray();

        partida.SetResult();
        var respostas = await Task.WhenAll(cruzadas);

        // Nenhuma pode ter virado erro de servidor. Recusa por saldo seria aceitavel se o
        // dinheiro acabasse, mas aqui cada conta so envia dez reais de cada vez.
        Assert.All(respostas, resposta => Assert.Equal(HttpStatusCode.Created, resposta.StatusCode));

        // Vinte de ida e vinte de volta, de dez reais: os saldos voltam ao ponto de partida.
        Assert.Equal(1_000m, (await cliente.Detalhe(contaA))!.Saldo);
        Assert.Equal(1_000m, (await cliente.Detalhe(contaB))!.Saldo);

        Assert.True((await cliente.Conciliacao(contaA))!.Bate);
        Assert.True((await cliente.Conciliacao(contaB))!.Bate);
    }

    /// <summary>
    /// Tres contas em ciclo — A para B, B para C, C para A —, que e a forma classica de
    /// deadlock que uma ordem parcial nao resolveria.
    /// </summary>
    [Fact]
    public async Task CicloDeTresContasTambemNaoTrava()
    {
        var cliente = fabrica.CreateClient();
        var contaA = await cliente.ContaCom(500m);
        var contaB = await cliente.ContaCom(500m);
        var contaC = await cliente.ContaCom(500m);

        var partida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var ciclo = Enumerable.Range(0, 15).SelectMany(_ => new[]
        {
            Disparar(cliente, partida, contaA, contaB),
            Disparar(cliente, partida, contaB, contaC),
            Disparar(cliente, partida, contaC, contaA),
        }).ToArray();

        partida.SetResult();
        var respostas = await Task.WhenAll(ciclo);

        Assert.All(respostas, resposta => Assert.Equal(HttpStatusCode.Created, resposta.StatusCode));

        Assert.Equal(500m, (await cliente.Detalhe(contaA))!.Saldo);
        Assert.Equal(500m, (await cliente.Detalhe(contaB))!.Saldo);
        Assert.Equal(500m, (await cliente.Detalhe(contaC))!.Saldo);

        foreach (var conta in new[] { contaA, contaB, contaC })
        {
            Assert.True((await cliente.Conciliacao(conta))!.Bate);
        }
    }

    /// <summary>
    /// Varias transferencias simultaneas saindo da mesma conta, somando mais do que ela
    /// tem: passam exatamente as que cabem, e o saldo nunca fica negativo.
    /// </summary>
    [Fact]
    public async Task SaidasSimultaneasParamNoSaldoDaOrigem()
    {
        var cliente = fabrica.CreateClient();
        var origem = await cliente.ContaCom(50m);
        var destino = await cliente.AbrirConta();

        var partida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saidas = Enumerable.Range(0, 10).Select(async _ =>
        {
            await partida.Task;
            return await cliente.Transferir(origem, destino.Id, 10m);
        }).ToArray();

        partida.SetResult();
        var respostas = await Task.WhenAll(saidas);

        Assert.Equal(5, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Created));

        var recusadas = respostas.Where(resposta => resposta.StatusCode != HttpStatusCode.Created).ToList();
        Assert.Equal(5, recusadas.Count);

        foreach (var recusada in recusadas)
        {
            Assert.Equal("Saldo insuficiente", await recusada.TituloDoProblema());
        }

        Assert.Equal(0m, (await cliente.Detalhe(origem))!.Saldo);
        Assert.Equal(50m, (await cliente.Detalhe(destino.Id))!.Saldo);
    }

    private static async Task<HttpResponseMessage> Disparar(
        HttpClient cliente,
        TaskCompletionSource partida,
        Guid origem,
        Guid destino)
    {
        await partida.Task;
        return await cliente.Transferir(origem, destino, 10m);
    }
}
