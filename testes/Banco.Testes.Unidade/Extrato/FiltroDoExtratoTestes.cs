using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Extrato;

namespace Banco.Testes.Unidade.Extrato;

public class FiltroDoExtratoTestes
{
    private static readonly DateTimeOffset Inicio = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Fim = new(2026, 9, 30, 23, 59, 59, TimeSpan.Zero);

    private static FiltroDoExtrato Filtro(
        DateTimeOffset? de = null,
        DateTimeOffset? ate = null,
        int tamanho = 50,
        MarcadorDoExtrato? marcador = null) =>
        new(Guid.NewGuid(), de ?? Inicio, ate ?? Fim, tamanho, marcador);

    [Fact]
    public void PeriodoDeUmInstanteSoEhValido() =>
        Filtro(de: Inicio, ate: Inicio).Validar();

    [Fact]
    public void PeriodoInvertidoEhRecusado() =>
        Assert.Throws<ConsultaInvalidaException>(() => Filtro(de: Fim, ate: Inicio).Validar());

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(FiltroDoExtrato.TamanhoMaximo + 1)]
    public void TamanhoForaDaFaixaEhRecusadoEmVezDeAparado(int tamanho) =>
        Assert.Throws<ConsultaInvalidaException>(() => Filtro(tamanho: tamanho).Validar());

    [Theory]
    [InlineData(1)]
    [InlineData(FiltroDoExtrato.TamanhoMaximo)]
    public void AsPontasDaFaixaDeTamanhoValem(int tamanho) => Filtro(tamanho: tamanho).Validar();

    /// <summary>
    /// Marcador de outro periodo pagina em cima de um recorte que nao e o pedido: a pagina
    /// seguinte sairia de uma lista diferente da que gerou o marcador.
    /// </summary>
    [Fact]
    public void MarcadorForaDoPeriodoEhRecusado() =>
        Assert.Throws<ConsultaInvalidaException>(
            () => Filtro(marcador: new MarcadorDoExtrato(Fim.AddDays(1), 10)).Validar());

    [Fact]
    public void MarcadorNaBordaDoPeriodoVale() =>
        Filtro(marcador: new MarcadorDoExtrato(Fim, 10)).Validar();
}
