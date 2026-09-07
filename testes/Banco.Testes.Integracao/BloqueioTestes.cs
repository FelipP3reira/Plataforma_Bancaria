using System.Net;
using System.Net.Http.Json;

namespace Banco.Testes.Integracao;

[Collection(ColecaoDaApi.Nome)]
public class BloqueioTestes
{
    private readonly FabricaDaApi fabrica;

    public BloqueioTestes(FabricaDaApi fabrica) => this.fabrica = fabrica;

    [Fact]
    public async Task BloquearBarraMovimentacaoEPreservaOExtrato()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(500m);

        var bloqueio = await cliente.Bloquear(conta, "suspeita de fraude");
        Assert.Equal(HttpStatusCode.OK, bloqueio.StatusCode);

        // Movimentar deixa de valer.
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.Sacar(conta, 10m)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.Depositar(conta, 10m)).StatusCode);

        // Consultar continua valendo, com saldo e historico intactos.
        var detalhe = await cliente.Detalhe(conta);
        Assert.Equal("Bloqueada", detalhe!.Estado);
        Assert.Equal(500m, detalhe.Saldo);
        Assert.Equal(1, detalhe.Lancamentos);
        Assert.True((await cliente.Conciliacao(conta))!.Bate);
    }

    [Fact]
    public async Task ARecusaDizQueEhBloqueioENaoOutraCoisa()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(500m);
        await cliente.Bloquear(conta, "suspeita");

        var recusada = await cliente.Sacar(conta, 10m);

        Assert.Equal("Conta nao movimenta", await recusada.TituloDoProblema());
    }

    [Fact]
    public async Task DesbloquearVoltaAMovimentar()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(100m);
        await cliente.Bloquear(conta, "suspeita");

        var desbloqueio = await cliente.Desbloquear(conta, "apurado, sem indicio");
        Assert.Equal(HttpStatusCode.OK, desbloqueio.StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await cliente.Depositar(conta, 50m)).StatusCode);
        Assert.Equal(150m, (await cliente.Detalhe(conta))!.Saldo);
    }

    [Fact]
    public async Task BloquearDuasVezesEhRecusado()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(10m);
        await cliente.Bloquear(conta, "suspeita");

        var segunda = await cliente.Bloquear(conta, "de novo");

        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal("Mudanca de estado invalida", await segunda.TituloDoProblema());
    }

    /// <summary>
    /// Bloqueio barra a transferencia dos dois lados, sem que a transferencia precise saber
    /// que existe bloqueio: a conferencia mora no agregado, no caminho de todo lancamento.
    /// </summary>
    [Fact]
    public async Task TransferenciaNaoEntraNemSaiDeContaBloqueada()
    {
        var cliente = fabrica.CreateClient();
        var bloqueada = await cliente.ContaCom(500m);
        var ativa = await cliente.ContaCom(500m);
        await cliente.Bloquear(bloqueada, "suspeita");

        var saindo = await cliente.Transferir(bloqueada, ativa, 10m);
        var entrando = await cliente.Transferir(ativa, bloqueada, 10m);

        Assert.Equal(HttpStatusCode.Conflict, saindo.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, entrando.StatusCode);

        // Nenhuma das duas contas pode ter se mexido.
        Assert.Equal(500m, (await cliente.Detalhe(bloqueada))!.Saldo);
        Assert.Equal(500m, (await cliente.Detalhe(ativa))!.Saldo);
    }

    [Fact]
    public async Task EncerrarComSaldoEhRecusado()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(10m);

        var resposta = await cliente.Encerrar(conta, "cliente pediu");

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("Ativa", (await cliente.Detalhe(conta))!.Estado);
    }

    [Fact]
    public async Task EncerrarZeradaFuncionaEDepoisNaoMovimentaMais()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(100m);
        await cliente.Sacar(conta, 100m);

        Assert.Equal(HttpStatusCode.OK, (await cliente.Encerrar(conta, "cliente pediu")).StatusCode);

        var detalhe = await cliente.Detalhe(conta);
        Assert.Equal("Encerrada", detalhe!.Estado);
        Assert.Equal(2, detalhe.Lancamentos);

        Assert.Equal(HttpStatusCode.Conflict, (await cliente.Depositar(conta, 1m)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.Desbloquear(conta, "reabrir")).StatusCode);
    }

    /// <summary>
    /// Encerrar nao apaga nada: o extrato e a trilha de estados continuam consultaveis.
    /// </summary>
    [Fact]
    public async Task ContaEncerradaContinuaAuditavel()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(80m);
        await cliente.Sacar(conta, 80m);
        await cliente.Bloquear(conta, "suspeita");
        await cliente.Encerrar(conta, "encerramento compulsorio");

        var historico = (await cliente.HistoricoDeEstado(conta))!;

        Assert.Equal(
            [("Ativa", "Bloqueada"), ("Bloqueada", "Encerrada")],
            historico.Select(mudanca => (mudanca.De, mudanca.Para)));

        Assert.Equal([1L, 2L], historico.Select(mudanca => mudanca.Sequencia));
        Assert.Equal("encerramento compulsorio", historico[^1].Motivo);
        Assert.Equal("operador:teste", historico[^1].Origem);

        Assert.True((await cliente.Conciliacao(conta))!.Bate);
    }

    [Fact]
    public async Task ContaSemMudancaTemHistoricoVazio()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();

        Assert.Empty((await cliente.HistoricoDeEstado(conta.Id))!);
    }

    [Fact]
    public async Task HistoricoDeContaInexistenteDevolveNaoEncontrada() =>
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await fabrica.CreateClient().GetAsync($"/contas/{Guid.NewGuid()}/estados")).StatusCode);

    [Fact]
    public async Task MudancaSemMotivoEhRecusada()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();

        var resposta = await cliente.PostAsJsonAsync($"/contas/{conta.Id}/bloqueio", new { motivo = "" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task MudancaSemOperadorEhRecusada()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();

        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/contas/{conta.Id}/bloqueio")
        {
            Content = JsonContent.Create(new { motivo = "suspeita" }),
        };

        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.SendAsync(pedido)).StatusCode);
    }

    [Fact]
    public async Task BloquearContaInexistenteDevolveNaoEncontrada() =>
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await fabrica.CreateClient().Bloquear(Guid.NewGuid(), "suspeita")).StatusCode);
}
