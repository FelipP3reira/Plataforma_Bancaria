using Banco.Dominio.Comum;
using Banco.Dominio.Contas;
using Banco.Dominio.Erros;
using Banco.Dominio.Ledger;

namespace Banco.Testes.Unidade.Contas;

public class ContaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static Conta Aberta(string titular = "Ana Ribeiro") =>
        Conta.Abrir(NumeroDaConta.DaSequencia(1001), titular, Agora);

    private static PedidoDeLancamento Pedido(
        decimal valor,
        string descricao = "movimentacao",
        string origem = "api:teste",
        string? chave = null,
        DateTimeOffset? quando = null) =>
        new(Dinheiro.De(valor), descricao, origem, chave ?? Guid.NewGuid().ToString(), quando ?? Agora);

    [Fact]
    public void ContaNasceZeradaESemLancamento()
    {
        var conta = Aberta();

        Assert.True(conta.Saldo.EhZero);
        Assert.Equal(0, conta.UltimaSequencia);
        Assert.Equal(Agora, conta.AbertaEm);
    }

    [Fact]
    public void RecusaTitularVazio()
    {
        Assert.Throws<ContaInvalidaException>(() => Aberta(" "));
        Assert.Throws<ContaInvalidaException>(() => Aberta(new string('a', Conta.TamanhoMaximoDoTitular + 1)));
    }

    [Fact]
    public void CreditarMoveOSaldoEGravaOSaldoDepois()
    {
        var conta = Aberta();

        var lancamento = conta.Creditar(Pedido(150.75m, "deposito"));

        Assert.Equal(TipoDeLancamento.Credito, lancamento.Tipo);
        Assert.Equal(150.75m, lancamento.Valor.Valor);
        Assert.Equal(150.75m, lancamento.SaldoDepois.Valor);
        Assert.Equal(150.75m, conta.Saldo.Valor);
        Assert.Equal(1, conta.UltimaSequencia);
    }

    [Fact]
    public void DebitarTiraDoSaldo()
    {
        var conta = Aberta();
        conta.Creditar(Pedido(200m));

        var lancamento = conta.Debitar(Pedido(75.50m, "saque"));

        Assert.Equal(TipoDeLancamento.Debito, lancamento.Tipo);
        Assert.Equal(124.50m, lancamento.SaldoDepois.Valor);
        Assert.Equal(124.50m, conta.Saldo.Valor);
        Assert.Equal(2, conta.UltimaSequencia);
    }

    [Fact]
    public void DebitarTudoDeixaZerado()
    {
        var conta = Aberta();
        conta.Creditar(Pedido(80m));

        conta.Debitar(Pedido(80m));

        Assert.True(conta.Saldo.EhZero);
    }

    /// <summary>
    /// Nao ha cheque especial. A conferencia mora no agregado, e nao so na borda: caso de
    /// uso novo que esqueca de checar antes esbarra aqui do mesmo jeito.
    /// </summary>
    [Fact]
    public void DebitarAlemDoSaldoEhRecusadoESemDeixarRastro()
    {
        var conta = Aberta();
        conta.Creditar(Pedido(100m));

        var erro = Assert.Throws<SaldoInsuficienteException>(() => conta.Debitar(Pedido(100.01m)));

        Assert.Contains("nao cobre", erro.Message, StringComparison.Ordinal);
        Assert.Equal(100m, conta.Saldo.Valor);
        Assert.Equal(1, conta.UltimaSequencia);
    }

    [Fact]
    public void DebitarDeContaZeradaEhRecusado() =>
        Assert.Throws<SaldoInsuficienteException>(() => Aberta().Debitar(Pedido(0.01m)));

    // A sequencia e a garantia estrutural do ledger: nao pode pular nem repetir.
    [Fact]
    public void SequenciaAndaDeUmEmUmNaOrdemDosLancamentos()
    {
        var conta = Aberta();
        var sequencias = new List<long>();

        for (var vez = 0; vez < 5; vez++)
        {
            sequencias.Add(conta.Creditar(Pedido(10m)).Sequencia);
        }

        Assert.Equal([1L, 2L, 3L, 4L, 5L], sequencias);
        Assert.Equal(5, conta.UltimaSequencia);
    }

    /// <summary>
    /// O saldo materializado e a soma do ledger tem que contar a mesma historia — e o
    /// SaldoDepois de cada linha tem que bater com o da anterior.
    /// </summary>
    [Fact]
    public void OSaldoMaterializadoBateComACorrenteDeLancamentos()
    {
        var conta = Aberta();
        var lancamentos = new List<Lancamento>();
        var sorteio = new Random(20260907);

        for (var vez = 0; vez < 200; vez++)
        {
            var valor = Math.Round(sorteio.Next(1, 50_000) / 100m, 2);

            lancamentos.Add(
                valor <= conta.Saldo.Valor && sorteio.Next(2) == 0
                    ? conta.Debitar(Pedido(valor))
                    : conta.Creditar(Pedido(valor)));
        }

        Assert.Equal(conta.Saldo.Valor, lancamentos.Sum(linha => linha.Efeito));
        Assert.Equal(conta.Saldo, lancamentos[^1].SaldoDepois);

        var acumulado = 0m;
        foreach (var lancamento in lancamentos)
        {
            acumulado += lancamento.Efeito;
            Assert.Equal(acumulado, lancamento.SaldoDepois.Valor);
        }
    }

    [Fact]
    public void MovimentacaoDeZeroNaoViraLancamento() =>
        Assert.Throws<ValorInvalidoException>(() => Aberta().Creditar(Pedido(0m)));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RecusaLancamentoSemChaveDeIdempotencia(string chave) =>
        Assert.Throws<ContaInvalidaException>(() => Aberta().Creditar(Pedido(10m, chave: chave)));

    [Fact]
    public void RecusaChaveLongaDemais() =>
        Assert.Throws<ContaInvalidaException>(
            () => Aberta().Creditar(
                Pedido(10m, chave: new string('k', PedidoDeLancamento.TamanhoMaximoDaChave + 1))));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RecusaLancamentoSemOrigem(string origem) =>
        Assert.Throws<ContaInvalidaException>(() => Aberta().Creditar(Pedido(10m, origem: origem)));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RecusaLancamentoSemDescricao(string descricao) =>
        Assert.Throws<ContaInvalidaException>(() => Aberta().Creditar(Pedido(10m, descricao: descricao)));

    [Fact]
    public void OLancamentoGuardaQuemPediuEQuando()
    {
        var quando = Agora.AddHours(3);

        var lancamento = Aberta().Creditar(
            Pedido(10m, origem: "operador:felipe", chave: "chave-1", quando: quando));

        Assert.Equal("operador:felipe", lancamento.Origem);
        Assert.Equal("chave-1", lancamento.ChaveIdempotencia);
        Assert.Equal(quando, lancamento.CriadoEm);
    }
}
