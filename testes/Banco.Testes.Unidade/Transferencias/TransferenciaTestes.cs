using Banco.Dominio.Comum;
using Banco.Dominio.Contas;
using Banco.Dominio.Erros;
using Banco.Dominio.Ledger;
using Banco.Dominio.Transferencias;

namespace Banco.Testes.Unidade.Transferencias;

public class TransferenciaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static Conta ContaCom(decimal saldo, long sequencia)
    {
        var conta = Conta.Abrir(NumeroDaConta.DaSequencia(sequencia), $"Titular {sequencia}", Agora);

        if (saldo > 0m)
        {
            conta.Creditar(Pedido(saldo, "abertura"));
        }

        return conta;
    }

    private static PedidoDeLancamento Pedido(decimal valor, string descricao = "transferencia") =>
        new(Dinheiro.De(valor), descricao, "operador:teste", Guid.NewGuid().ToString(), Agora);

    [Fact]
    public void DebitaAOrigemECreditaODestinoPeloMesmoValor()
    {
        var origem = ContaCom(500m, 1);
        var destino = ContaCom(0m, 2);

        var realizada = Transferencia.Entre(origem, destino, Pedido(120.40m));

        Assert.Equal(379.60m, origem.Saldo.Valor);
        Assert.Equal(120.40m, destino.Saldo.Valor);
        Assert.Equal(TipoDeLancamento.Debito, realizada.Debito.Tipo);
        Assert.Equal(TipoDeLancamento.Credito, realizada.Credito.Tipo);
        Assert.Equal(realizada.Debito.Valor, realizada.Credito.Valor);
    }

    // O que sai de um lado tem que entrar do outro: a soma dos dois efeitos e zero.
    [Fact]
    public void ATransferenciaNaoCriaNemDestroiDinheiro()
    {
        var origem = ContaCom(500m, 1);
        var destino = ContaCom(300m, 2);
        var antes = origem.Saldo.Valor + destino.Saldo.Valor;

        var realizada = Transferencia.Entre(origem, destino, Pedido(77.77m));

        Assert.Equal(0m, realizada.Debito.Efeito + realizada.Credito.Efeito);
        Assert.Equal(antes, origem.Saldo.Valor + destino.Saldo.Valor);
    }

    [Fact]
    public void AsDuasPernasApontamParaAMesmaTransferencia()
    {
        var realizada = Transferencia.Entre(ContaCom(100m, 1), ContaCom(0m, 2), Pedido(50m));

        Assert.Equal(realizada.Transferencia.Id, realizada.Debito.TransferenciaId);
        Assert.Equal(realizada.Transferencia.Id, realizada.Credito.TransferenciaId);
    }

    /// <summary>
    /// As pernas nao podem levar a chave do cliente. Ela vale por conta, e a de credito cai
    /// numa conta que nunca viu essa chave.
    /// </summary>
    [Fact]
    public void AsPernasNaoCarregamAChaveDoCliente()
    {
        var pedido = Pedido(50m) with { ChaveIdempotencia = "chave-do-cliente" };

        var realizada = Transferencia.Entre(ContaCom(100m, 1), ContaCom(0m, 2), pedido);

        Assert.Equal("chave-do-cliente", realizada.Transferencia.ChaveIdempotencia);
        Assert.NotEqual("chave-do-cliente", realizada.Debito.ChaveIdempotencia);
        Assert.NotEqual("chave-do-cliente", realizada.Credito.ChaveIdempotencia);
        Assert.NotEqual(realizada.Debito.ChaveIdempotencia, realizada.Credito.ChaveIdempotencia);
    }

    [Fact]
    public void AChaveDeCadaPernaCabeNaColuna()
    {
        var realizada = Transferencia.Entre(ContaCom(100m, 1), ContaCom(0m, 2), Pedido(50m));

        Assert.True(realizada.Debito.ChaveIdempotencia.Length <= PedidoDeLancamento.TamanhoMaximoDaChave);
        Assert.True(realizada.Credito.ChaveIdempotencia.Length <= PedidoDeLancamento.TamanhoMaximoDaChave);
    }

    /// <summary>
    /// O debito vem primeiro porque e o unico que pode ser recusado. Se o credito viesse
    /// antes, o destino ficaria com dinheiro que a origem nao tinha.
    /// </summary>
    [Fact]
    public void SemSaldoNaOrigemODestinoNaoEncostaNoDinheiro()
    {
        var origem = ContaCom(100m, 1);
        var destino = ContaCom(0m, 2);

        Assert.Throws<SaldoInsuficienteException>(
            () => Transferencia.Entre(origem, destino, Pedido(100.01m)));

        Assert.Equal(100m, origem.Saldo.Valor);
        Assert.True(destino.Saldo.EhZero);
        Assert.Equal(0, destino.UltimaSequencia);
    }

    [Fact]
    public void TransferirParaSiMesmoEhRecusado()
    {
        var conta = ContaCom(100m, 1);

        var erro = Assert.Throws<ContaInvalidaException>(
            () => Transferencia.Entre(conta, conta, Pedido(10m)));

        Assert.Contains("mesma conta", erro.Message, StringComparison.Ordinal);
        Assert.Equal(100m, conta.Saldo.Valor);
    }

    [Fact]
    public void ATransferenciaGuardaQuemPediuEQuando()
    {
        var origem = ContaCom(100m, 1);
        var destino = ContaCom(0m, 2);

        var realizada = Transferencia.Entre(origem, destino, Pedido(30m, "aluguel"));

        Assert.Equal(origem.Id, realizada.Transferencia.ContaOrigemId);
        Assert.Equal(destino.Id, realizada.Transferencia.ContaDestinoId);
        Assert.Equal(30m, realizada.Transferencia.Valor.Valor);
        Assert.Equal("aluguel", realizada.Transferencia.Descricao);
        Assert.Equal("operador:teste", realizada.Transferencia.Origem);
        Assert.Equal(Agora, realizada.Transferencia.CriadaEm);
    }

    [Fact]
    public void TransferirTudoDeixaAOrigemZerada()
    {
        var origem = ContaCom(250m, 1);
        var destino = ContaCom(0m, 2);

        Transferencia.Entre(origem, destino, Pedido(250m));

        Assert.True(origem.Saldo.EhZero);
        Assert.Equal(250m, destino.Saldo.Valor);
    }
}
