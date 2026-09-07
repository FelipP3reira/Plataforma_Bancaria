using Banco.Dominio.Comum;
using Banco.Dominio.Erros;

namespace Banco.Testes.Unidade.Comum;

public class DinheiroTestes
{
    [Theory]
    [InlineData(0)]
    [InlineData(0.01)]
    [InlineData(1234.56)]
    [InlineData(99999999999999.99)]
    public void AceitaValorNaoNegativoComAteDuasCasas(decimal valor) =>
        Assert.Equal(valor, Dinheiro.De(valor).Valor);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    public void RecusaValorNegativo(decimal valor) =>
        Assert.Throws<ValorInvalidoException>(() => Dinheiro.De(valor));

    // Arredondar em silencio faria o extrato mostrar valor diferente do que o cliente mandou.
    [Theory]
    [InlineData(10.999)]
    [InlineData(0.001)]
    [InlineData(1.005)]
    public void RecusaMaisDeDuasCasasEmVezDeArredondar(decimal valor)
    {
        var erro = Assert.Throws<ValorInvalidoException>(() => Dinheiro.De(valor));

        Assert.Contains("casas decimais", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SomaSemPerderCentavo()
    {
        var total = Dinheiro.Zero;

        // A mesma soma em double daria 0,9999999999999999.
        for (var vez = 0; vez < 10; vez++)
        {
            total += Dinheiro.De(0.10m);
        }

        Assert.Equal(1.00m, total.Valor);
    }

    [Fact]
    public void SubtraiAteZero() =>
        Assert.True((Dinheiro.De(50m) - Dinheiro.De(50m)).EhZero);

    /// <summary>
    /// A propria subtracao recusa: assim nenhum caminho de codigo consegue produzir saldo
    /// negativo, nem um caso de uso futuro que esqueca de conferir antes.
    /// </summary>
    [Fact]
    public void SubtrairAlemDoQueExisteEhRecusado()
    {
        var erro = Assert.Throws<ValorInvalidoException>(() => Dinheiro.De(50m) - Dinheiro.De(50.01m));

        Assert.Contains("negativo", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparaPorValor()
    {
        Assert.True(Dinheiro.De(10m) < Dinheiro.De(10.01m));
        Assert.True(Dinheiro.De(10m) <= Dinheiro.De(10m));
        Assert.True(Dinheiro.De(10.01m) > Dinheiro.De(10m));
        Assert.True(Dinheiro.De(10m) >= Dinheiro.De(10m));
    }

    [Fact]
    public void DoisValoresIguaisSaoOMesmoDinheiro() =>
        Assert.Equal(Dinheiro.De(12.34m), Dinheiro.De(12.34m));

    // O texto vai para mensagem de erro e log: nao pode mudar com a configuracao da maquina.
    [Fact]
    public void FormataEmRealIndependenteDaCulturaDaMaquina()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");

        try
        {
            Assert.Equal("R$ 1.234,56", Dinheiro.De(1234.56m).ToString());
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
