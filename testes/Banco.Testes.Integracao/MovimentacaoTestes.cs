using System.Net;
using System.Net.Http.Json;

namespace Banco.Testes.Integracao;

[Collection(ColecaoDaApi.Nome)]
public class MovimentacaoTestes
{
    private readonly FabricaDaApi fabrica;

    public MovimentacaoTestes(FabricaDaApi fabrica) => this.fabrica = fabrica;

    [Fact]
    public async Task ContaNasceComNumeroValidoESaldoZero()
    {
        var conta = await fabrica.CreateClient().AbrirConta("Bruno Lima");

        Assert.Equal(0m, conta.Saldo);
        Assert.Equal("Bruno Lima", conta.Titular);
        Assert.Matches(@"^\d{7}-\d$", conta.Numero);
    }

    [Fact]
    public async Task NumeroDeContaNaoSeRepete()
    {
        var cliente = fabrica.CreateClient();

        var numeros = new List<string>();
        for (var vez = 0; vez < 5; vez++)
        {
            numeros.Add((await cliente.AbrirConta()).Numero);
        }

        Assert.Equal(numeros.Count, numeros.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task DepositoGravaLancamentoEMoveOSaldo()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();

        var resposta = await cliente.Depositar(conta.Id, 250.50m, descricao: "salario");

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);

        var lancamento = await resposta.Lancamento();
        Assert.Equal("Credito", lancamento!.Tipo);
        Assert.Equal(1, lancamento.Sequencia);
        Assert.Equal(250.50m, lancamento.SaldoDepois);
        Assert.Equal(250.50m, lancamento.Efeito);
        Assert.Equal("operador:teste", lancamento.Origem);

        Assert.Equal(250.50m, (await cliente.Detalhe(conta.Id))!.Saldo);
    }

    [Fact]
    public async Task SaqueTiraDoSaldoEGravaEfeitoNegativo()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(400m);

        var lancamento = await (await cliente.Sacar(conta, 125.25m)).Lancamento();

        Assert.Equal("Debito", lancamento!.Tipo);
        Assert.Equal(125.25m, lancamento.Valor);
        Assert.Equal(-125.25m, lancamento.Efeito);
        Assert.Equal(274.75m, lancamento.SaldoDepois);
    }

    [Fact]
    public async Task SaqueAlemDoSaldoEhRecusadoESemMexerNaConta()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(100m);

        var resposta = await cliente.Sacar(conta, 100.01m);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);

        var detalhe = await cliente.Detalhe(conta);
        Assert.Equal(100m, detalhe!.Saldo);
        Assert.Equal(1, detalhe.Lancamentos);
    }

    [Fact]
    public async Task SaqueEmContaZeradaEhRecusado()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();

        Assert.Equal(HttpStatusCode.Conflict, (await cliente.Sacar(conta.Id, 0.01m)).StatusCode);
    }

    /// <summary>
    /// Cliente que perdeu a resposta por tempo esgotado reenvia o mesmo deposito. Isso
    /// precisa ser reconhecido como repeticao, e nao como um segundo deposito.
    /// </summary>
    [Fact]
    public async Task ReenvioComAMesmaChaveNaoDepositaDeNovo()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();
        var chave = Pedidos.ChaveNova();

        var primeira = await cliente.Depositar(conta.Id, 300m, chave);
        var segunda = await cliente.Depositar(conta.Id, 300m, chave);

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);

        Assert.Equal((await primeira.Lancamento())!.Id, (await segunda.Lancamento())!.Id);

        var detalhe = await cliente.Detalhe(conta.Id);
        Assert.Equal(300m, detalhe!.Saldo);
        Assert.Equal(1, detalhe.Lancamentos);
    }

    // Mesma chave com outro valor nao e reenvio: e chave reaproveitada por engano.
    [Fact]
    public async Task MesmaChaveComValorDiferenteEhRecusada()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();
        var chave = Pedidos.ChaveNova();

        await cliente.Depositar(conta.Id, 100m, chave);
        var resposta = await cliente.Depositar(conta.Id, 999m, chave);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal(100m, (await cliente.Detalhe(conta.Id))!.Saldo);
    }

    // A chave vale por conta: dois clientes diferentes podem mandar a mesma sem colidir.
    [Fact]
    public async Task AMesmaChaveEmContasDiferentesSaoOperacoesDiferentes()
    {
        var cliente = fabrica.CreateClient();
        var primeira = await cliente.AbrirConta();
        var segunda = await cliente.AbrirConta();
        var chave = Pedidos.ChaveNova();

        Assert.Equal(HttpStatusCode.Created, (await cliente.Depositar(primeira.Id, 10m, chave)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await cliente.Depositar(segunda.Id, 10m, chave)).StatusCode);
    }

    [Fact]
    public async Task MovimentacaoSemChaveDeIdempotenciaEhRecusada()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();

        var resposta = await cliente.PostAsJsonAsync(
            $"/contas/{conta.Id}/depositos",
            new { valor = 10m, descricao = "sem chave" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task MovimentacaoSemOperadorEhRecusada()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();

        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/contas/{conta.Id}/depositos")
        {
            Content = JsonContent.Create(new { valor = 10m, descricao = "sem operador" }),
        };
        pedido.Headers.Add("Idempotency-Key", Pedidos.ChaveNova());

        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.SendAsync(pedido)).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(10.999)]
    public async Task ValorInvalidoEhRecusadoNaBorda(decimal valor)
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();

        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.Depositar(conta.Id, valor)).StatusCode);
    }

    [Fact]
    public async Task ContaInexistenteDevolveNaoEncontrada()
    {
        var cliente = fabrica.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await cliente.Depositar(Guid.NewGuid(), 10m)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await cliente.GetAsync($"/contas/{Guid.NewGuid()}")).StatusCode);
    }

    // A sequencia e a garantia estrutural do ledger: nao pode pular nem repetir.
    [Fact]
    public async Task ASequenciaAndaDeUmEmUmNaConta()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();

        var sequencias = new List<long>();
        for (var vez = 0; vez < 6; vez++)
        {
            sequencias.Add((await (await cliente.Depositar(conta.Id, 5m)).Lancamento())!.Sequencia);
        }

        Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L], sequencias);
        Assert.Equal(6, (await cliente.Detalhe(conta.Id))!.Lancamentos);
    }
}
