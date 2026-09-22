using Banco.Dominio.Documentos.Boletos;
using Banco.Dominio.Erros;

namespace Banco.Testes.Unidade.Documentos;

public class CnpjTestes
{
    internal const string Valido = "11222333000181";

    /// <summary>
    /// O exemplo que a Receita publicou ao especificar o CNPJ alfanumerico.
    /// </summary>
    /// <remarks>
    /// Ancora externa: se a conta do digito estivesse errada, um CNPJ gerado pela propria conta
    /// passaria nos testes de qualquer jeito. Este vem de fora.
    /// </remarks>
    internal const string Alfanumerico = "12ABC34501DE35";

    [Fact]
    public void AceitaCnpjNumerico() => Assert.Equal(Valido, Cnpj.DoTexto(Valido).Texto);

    [Fact]
    public void AceitaOExemploAlfanumericoDaReceita() =>
        Assert.Equal(Alfanumerico, Cnpj.DoTexto("12.ABC.345/01DE-35").Texto);

    [Theory]
    [InlineData("11.222.333/0001-81")]
    [InlineData("11222333/0001-81")]
    [InlineData(" 11222333000181 ")]
    public void APontuacaoNaoImporta(string entrada) =>
        Assert.Equal(Valido, Cnpj.DoTexto(entrada).Texto);

    [Fact]
    public void LetraMinusculaViraMaiuscula() =>
        Assert.Equal(Alfanumerico, Cnpj.DoTexto("12abc34501de35").Texto);

    /// <summary>
    /// O digito e o que separa "leu um CNPJ" de "leu catorze caracteres". Numa nota fiscal
    /// qualquer sequencia longa parece CNPJ — numero da nota, inscricao estadual, codigo de
    /// produto — e sem os verificadores o campo do emissor receberia o primeiro numero grande da
    /// pagina.
    /// </summary>
    [Theory]
    [InlineData("11222333000182")]
    [InlineData("11222333000191")]
    [InlineData("11222333000180")]
    public void DigitoErradoEhRecusado(string entrada) =>
        Assert.False(Cnpj.TentarLer(entrada, out _));

    /// <summary>
    /// Sequencia repetida passa na conta e nao existe na Receita — e e exatamente o que um
    /// modelo devolve quando le um campo em branco.
    /// </summary>
    [Theory]
    [InlineData("00000000000000")]
    [InlineData("11111111111111")]
    public void SequenciaRepetidaEhRecusada(string entrada) =>
        Assert.False(Cnpj.TentarLer(entrada, out _));

    /// <summary>
    /// Os dois ultimos caracteres sao resultado de um modulo 11, e o resultado nunca e letra.
    /// </summary>
    [Fact]
    public void LetraNaPosicaoDoVerificadorEhRecusada() =>
        Assert.False(Cnpj.TentarLer("12ABC34501DEA5", out _));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1122233300018")]
    [InlineData("112223330001811")]
    public void TamanhoErradoEhRecusado(string? entrada) =>
        Assert.False(Cnpj.TentarLer(entrada, out _));

    [Fact]
    public void CnpjInvalidoLancaNaLeituraDireta() =>
        Assert.Throws<BoletoInvalidoException>(() => Cnpj.DoTexto("11222333000182"));

    [Fact]
    public void FormataParaExibir() =>
        Assert.Equal("11.222.333/0001-81", Cnpj.DoTexto(Valido).Formatado());
}
