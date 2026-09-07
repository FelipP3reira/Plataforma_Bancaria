using System.Net;
using System.Net.Http.Json;

namespace Banco.Testes.Integracao;

[Collection(ColecaoDaApi.Nome)]
public class TransferenciaTestes
{
    private readonly FabricaDaApi fabrica;

    public TransferenciaTestes(FabricaDaApi fabrica) => this.fabrica = fabrica;

    [Fact]
    public async Task TransfereDebitandoUmaECreditandoAOutra()
    {
        var cliente = fabrica.CreateClient();
        var origem = await cliente.ContaCom(500m);
        var destino = await cliente.ContaCom(100m);

        var resposta = await cliente.Transferir(origem, destino, 175.25m);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);

        var transferencia = await resposta.Transferencia();
        Assert.Equal(origem, transferencia!.ContaOrigemId);
        Assert.Equal(destino, transferencia.ContaDestinoId);
        Assert.Equal(175.25m, transferencia.Valor);

        Assert.Equal(324.75m, (await cliente.Detalhe(origem))!.Saldo);
        Assert.Equal(275.25m, (await cliente.Detalhe(destino))!.Saldo);
    }

    /// <summary>
    /// O debito acontece antes do credito. Sem saldo, nada pode ter sido gravado — nem a
    /// perna de credito, nem o registro da transferencia.
    /// </summary>
    [Fact]
    public async Task SemSaldoNaOrigemNadaEhGravadoEmNenhumaDasDuas()
    {
        var cliente = fabrica.CreateClient();
        var origem = await cliente.ContaCom(50m);
        var destino = await cliente.ContaCom(200m);

        var resposta = await cliente.Transferir(origem, destino, 50.01m);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("Saldo insuficiente", await resposta.TituloDoProblema());

        var contaOrigem = await cliente.Detalhe(origem);
        var contaDestino = await cliente.Detalhe(destino);

        Assert.Equal(50m, contaOrigem!.Saldo);
        Assert.Equal(1, contaOrigem.Lancamentos);
        Assert.Equal(200m, contaDestino!.Saldo);
        Assert.Equal(1, contaDestino.Lancamentos);
    }

    [Fact]
    public async Task TransferirParaSiMesmoEhRecusado()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(100m);

        var resposta = await cliente.Transferir(conta, conta, 10m);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(100m, (await cliente.Detalhe(conta))!.Saldo);
    }

    [Fact]
    public async Task ContaInexistenteDevolveNaoEncontrada()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(100m);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await cliente.Transferir(conta, Guid.NewGuid(), 10m)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await cliente.Transferir(Guid.NewGuid(), conta, 10m)).StatusCode);

        // Nem a conta que existe pode ter sido tocada nas duas tentativas.
        Assert.Equal(100m, (await cliente.Detalhe(conta))!.Saldo);
    }

    /// <summary>
    /// As duas pernas apontam para a mesma transferencia, e a conciliacao das duas contas
    /// continua fechando.
    /// </summary>
    [Fact]
    public async Task AsDuasPernasFicamLigadasEOLedgerContinuaIntegro()
    {
        var cliente = fabrica.CreateClient();
        var origem = await cliente.ContaCom(1_000m);
        var destino = await cliente.AbrirConta();

        await cliente.Transferir(origem, destino.Id, 333.33m);

        Assert.True((await cliente.Conciliacao(origem))!.Bate);
        Assert.True((await cliente.Conciliacao(destino.Id))!.Bate);

        Assert.Equal(666.67m, (await cliente.Detalhe(origem))!.Saldo);
        Assert.Equal(333.33m, (await cliente.Detalhe(destino.Id))!.Saldo);
    }

    [Fact]
    public async Task ReenvioComAMesmaChaveNaoTransfereDeNovo()
    {
        var cliente = fabrica.CreateClient();
        var origem = await cliente.ContaCom(500m);
        var destino = await cliente.AbrirConta();
        var chave = Pedidos.ChaveNova();

        var primeira = await cliente.Transferir(origem, destino.Id, 200m, chave);
        var segunda = await cliente.Transferir(origem, destino.Id, 200m, chave);

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.Equal((await primeira.Transferencia())!.Id, (await segunda.Transferencia())!.Id);

        Assert.Equal(300m, (await cliente.Detalhe(origem))!.Saldo);
        Assert.Equal(200m, (await cliente.Detalhe(destino.Id))!.Saldo);
    }

    [Fact]
    public async Task MesmaChaveComOutroValorEhRecusada()
    {
        var cliente = fabrica.CreateClient();
        var origem = await cliente.ContaCom(500m);
        var destino = await cliente.AbrirConta();
        var chave = Pedidos.ChaveNova();

        await cliente.Transferir(origem, destino.Id, 100m, chave);
        var resposta = await cliente.Transferir(origem, destino.Id, 250m, chave);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("Chave de idempotencia reutilizada", await resposta.TituloDoProblema());
        Assert.Equal(400m, (await cliente.Detalhe(origem))!.Saldo);
    }

    [Fact]
    public async Task ConsultaATransferenciaPeloId()
    {
        var cliente = fabrica.CreateClient();
        var origem = await cliente.ContaCom(100m);
        var destino = await cliente.AbrirConta();

        var criada = await (await cliente.Transferir(origem, destino.Id, 60m, descricao: "aluguel"))
            .Transferencia();

        var lida = await cliente.GetFromJsonAsync<TransferenciaHttpResposta>(
            $"/transferencias/{criada!.Id}",
            Pedidos.Json);

        Assert.Equal(criada.Id, lida!.Id);
        Assert.Equal("aluguel", lida.Descricao);
        Assert.Equal(60m, lida.Valor);
    }

    [Fact]
    public async Task TransferenciaInexistenteDevolveNaoEncontrada() =>
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await fabrica.CreateClient().GetAsync($"/transferencias/{Guid.NewGuid()}")).StatusCode);

    [Fact]
    public async Task TransferenciaSemChaveDeIdempotenciaEhRecusada()
    {
        var cliente = fabrica.CreateClient();
        var origem = await cliente.ContaCom(100m);
        var destino = await cliente.AbrirConta();

        var resposta = await cliente.PostAsJsonAsync(
            $"/contas/{origem}/transferencias",
            new { contaDestinoId = destino.Id, valor = 10m, descricao = "sem chave" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.001)]
    public async Task ValorInvalidoEhRecusadoNaBorda(decimal valor)
    {
        var cliente = fabrica.CreateClient();
        var origem = await cliente.ContaCom(100m);
        var destino = await cliente.AbrirConta();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await cliente.Transferir(origem, destino.Id, valor)).StatusCode);
    }
}
