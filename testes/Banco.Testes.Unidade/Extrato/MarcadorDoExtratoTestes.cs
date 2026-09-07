using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Extrato;

namespace Banco.Testes.Unidade.Extrato;

public class MarcadorDoExtratoTestes
{
    private static readonly DateTimeOffset Instante =
        new(2026, 9, 7, 12, 30, 45, 123, TimeSpan.FromHours(-3));

    [Fact]
    public void CodificarEDecodificarDevolveOMesmoMarcador()
    {
        var marcador = new MarcadorDoExtrato(Instante, 4321);

        var voltou = MarcadorDoExtrato.Decodificar(marcador.Codificar());

        Assert.Equal(marcador.CriadoEm, voltou.CriadoEm);
        Assert.Equal(marcador.Sequencia, voltou.Sequencia);
    }

    /// <summary>
    /// O marcador viaja na query string. Se sobrar <c>+</c> ou <c>/</c>, um cliente que
    /// esqueca o escape manda um marcador diferente do que recebeu.
    /// </summary>
    [Fact]
    public void OTextoNaoTemCaractereQuePrecisaDeEscapeNaUrl()
    {
        var codificado = new MarcadorDoExtrato(Instante, long.MaxValue).Codificar();

        Assert.DoesNotContain("+", codificado, StringComparison.Ordinal);
        Assert.DoesNotContain("/", codificado, StringComparison.Ordinal);
        Assert.DoesNotContain("=", codificado, StringComparison.Ordinal);
    }

    // O deslocamento faz parte do instante: perde-lo moveria a pagina em tres horas.
    [Fact]
    public void ODeslocamentoDeFusoSobrevive() =>
        Assert.Equal(
            Instante.Offset,
            MarcadorDoExtrato.Decodificar(new MarcadorDoExtrato(Instante, 1).Codificar()).CriadoEm.Offset);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nao-e-base64-!!!")]
    [InlineData("c2Vt")]
    public void MarcadorIlegivelEhErroDoPedido(string codificado) =>
        Assert.Throws<ConsultaInvalidaException>(() => MarcadorDoExtrato.Decodificar(codificado));
}
