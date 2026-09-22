using Banco.Dominio.Documentos.Boletos;
using Banco.Dominio.Erros;

namespace Banco.Testes.Unidade.Documentos;

/// <summary>
/// A linha digitavel e os quatro digitos que a tornam verificavel.
/// </summary>
public class LinhaDigitavelTestes
{
    /// <summary>Boleto de R$ 189,90 com vencimento em 10/10/2026.</summary>
    internal const string Padrao = "00191234546789012345767890123457915950000018990";

    /// <summary>O mesmo boleto com valor zerado — valor a combinar.</summary>
    internal const string SemValor = "23791234546789012345767890123457615950000000000";

    /// <summary>Fator de vencimento zero: boleto pagavel a vista.</summary>
    internal const string SemVencimento = "34191234546789012345767890123457500000000010000";

    /// <summary>
    /// O exemplo publicado pela FEBRABAN na especificacao do codigo de barras.
    /// </summary>
    /// <remarks>
    /// Existe aqui como ancora externa. Todo o resto das linhas destes testes foi gerado pela
    /// mesma regra que o codigo aplica — se a regra estivesse errada, os dois estariam errados
    /// juntos e os testes passariam. Este vem de fora, e e o unico que prova que a conta e a
    /// conta certa.
    /// </remarks>
    internal const string DaFebraban = "00190500954014481606906809350314337370000000100";

    [Fact]
    public void LeOsCamposDoBoletoPadrao()
    {
        var linha = LinhaDigitavel.DoTexto(Padrao);

        Assert.Equal("001", linha.Banco);
        Assert.Equal('9', linha.Moeda);
        Assert.Equal(18990, linha.ValorEmCentavos);
        Assert.Equal(189.90m, linha.Valor!.Value.Valor);
    }

    /// <summary>
    /// O exemplo da FEBRABAN fecha os quatro digitos. Se esta afirmacao cair, o erro esta na
    /// conta e nao no boleto.
    /// </summary>
    [Fact]
    public void OExemploDaFebrabanFecha()
    {
        var linha = LinhaDigitavel.DoTexto(DaFebraban);

        Assert.Equal("001", linha.Banco);
        Assert.Equal(100, linha.ValorEmCentavos);
        Assert.Equal("00193373700000001000500940144816060680935031", linha.CodigoDeBarras);
    }

    /// <summary>
    /// A linha digitavel e uma reordenacao do codigo de barras. Remontar na ordem original e o
    /// que permite conferir o digito geral, que so existe la.
    /// </summary>
    [Fact]
    public void RemontaOCodigoDeBarrasComQuarentaEQuatroAlgarismos()
    {
        var linha = LinhaDigitavel.DoTexto(Padrao);

        Assert.Equal(LinhaDigitavel.AlgarismosDoCodigoDeBarras, linha.CodigoDeBarras.Length);

        // Banco e moeda abrem os dois; o valor muda de lugar.
        Assert.StartsWith("0019", linha.CodigoDeBarras, StringComparison.Ordinal);
        Assert.EndsWith("0000018990", linha.Digitos, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pontuacao que o banco imprime nao participa: quem digita copia com pontos, e o leitor
    /// de imagem devolve o que estava la.
    /// </summary>
    [Theory]
    [InlineData("00191.23454 67890.123457 67890.123457 9 15950000018990")]
    [InlineData("  00191234546789012345767890123457915950000018990  ")]
    [InlineData("0019123454-6789012345767890123457915950000018990")]
    public void APontuacaoNaoImporta(string entrada) =>
        Assert.Equal(Padrao, LinhaDigitavel.DoTexto(entrada).Digitos);

    /// <summary>
    /// Um algarismo trocado em qualquer campo derruba a leitura. E o que faz a extracao ser
    /// verificavel: o modelo que le "8" onde estava "3" nao sabe que errou, o digito sabe.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(25)]
    [InlineData(40)]
    public void UmAlgarismoTrocadoNaoFecha(int posicao)
    {
        var quebrada = Padrao.ToCharArray();
        quebrada[posicao] = quebrada[posicao] == '9' ? '8' : (char)(quebrada[posicao] + 1);

        Assert.Throws<BoletoInvalidoException>(() => LinhaDigitavel.DoTexto(new string(quebrada)));
    }

    /// <summary>
    /// Trocar dois algarismos vizinhos de lugar tambem cai. E o erro de digitacao mais comum
    /// depois do algarismo trocado, e um verificador de peso fixo deixaria passar.
    /// </summary>
    [Fact]
    public void DoisAlgarismosTrocadosDeLugarNaoFecham()
    {
        var quebrada = Padrao.ToCharArray();
        (quebrada[4], quebrada[5]) = (quebrada[5], quebrada[4]);

        Assert.Throws<BoletoInvalidoException>(() => LinhaDigitavel.DoTexto(new string(quebrada)));
    }

    /// <summary>
    /// Conta de luz, de agua e de tributo tem 48 algarismos e outro padrao inteiro — outra
    /// posicao de valor, outro verificador. Recusar dizendo o que e vale mais do que adivinhar:
    /// ler o valor da posicao errada pagaria o numero errado.
    /// </summary>
    [Fact]
    public void ContaDeConcessionariaEhRecusadaDizendoOQueEh()
    {
        var erro = Assert.Throws<BoletoInvalidoException>(
            () => LinhaDigitavel.DoTexto(new string('8', LinhaDigitavel.AlgarismosDeConcessionaria)));

        Assert.Contains("concessionaria", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("0019123454678901234576789012345791595000001899")]
    public void TamanhoErradoEhRecusado(string entrada) =>
        Assert.Throws<BoletoInvalidoException>(() => LinhaDigitavel.DoTexto(entrada));

    [Fact]
    public void ValorZeradoNoCodigoViraValorAusente()
    {
        var linha = LinhaDigitavel.DoTexto(SemValor);

        Assert.Equal(0, linha.ValorEmCentavos);
        Assert.Null(linha.Valor);
    }

    [Fact]
    public void FatorZeroEhBoletoAVista() =>
        Assert.Equal(
            FatorDeVencimento.SemVencimento,
            LinhaDigitavel.DoTexto(SemVencimento).Fator);

    [Fact]
    public void TentarLerNaoLancaParaLinhaInvalida()
    {
        Assert.False(LinhaDigitavel.TentarLer("nada disso", out _));
        Assert.True(LinhaDigitavel.TentarLer(Padrao, out var linha));
        Assert.Equal(Padrao, linha.Digitos);
    }
}

public class DigitoVerificadorTestes
{
    /// <summary>
    /// Os tres campos do exemplo da FEBRABAN, conferidos um a um.
    /// </summary>
    [Theory]
    [InlineData("001905009", '5')]
    [InlineData("4014481606", '9')]
    [InlineData("0680935031", '4')]
    public void OModulo10FechaOsCamposDoExemploPublicado(string campo, char esperado) =>
        Assert.Equal(esperado, DigitoVerificador.Modulo10(campo));

    /// <summary>
    /// O digito geral do codigo de barras do exemplo da FEBRABAN.
    /// </summary>
    [Fact]
    public void OModulo11FechaOCodigoDeBarrasDoExemploPublicado() =>
        Assert.Equal('3', DigitoVerificador.Modulo11("0019" + "37370000000100" + "0500940144816060680935031"));

    /// <summary>
    /// Resto 0, 10 ou 11 vira digito 1 por especificacao: os tres cairiam fora da faixa de um
    /// algarismo, e a FEBRABAN escolheu colapsar em vez de proibir as chaves que os produzem.
    /// </summary>
    [Fact]
    public void OModulo11NuncaDevolveDoisAlgarismos()
    {
        foreach (var digitos in Enumerable.Range(0, 200).Select(numero => new string('1', 43 - (numero % 40)) + numero))
        {
            Assert.True(char.IsAsciiDigit(DigitoVerificador.Modulo11(digitos)));
        }
    }

    [Fact]
    public void OModulo10NuncaDevolveDoisAlgarismos()
    {
        foreach (var digitos in Enumerable.Range(0, 200).Select(numero => numero.ToString(null as IFormatProvider)))
        {
            Assert.True(char.IsAsciiDigit(DigitoVerificador.Modulo10(digitos)));
        }
    }
}
