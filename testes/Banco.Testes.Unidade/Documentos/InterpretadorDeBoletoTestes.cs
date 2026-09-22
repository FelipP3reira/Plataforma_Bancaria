using Banco.Dominio.Documentos.Boletos;

namespace Banco.Testes.Unidade.Documentos;

/// <summary>
/// A leitura do texto extraido, e a ideia que a organiza: a estrutura confere o modelo.
/// </summary>
public class InterpretadorDeBoletoTestes
{
    private static readonly DateOnly Hoje = new(2026, 9, 22);

    private static LeituraDoDocumento Ler(string texto) =>
        InterpretadorDeBoleto.Interpretar(texto, Hoje);

    private static CampoLido Campo(string texto, NomeDoCampo nome) =>
        Ler(texto).Campo(nome) ?? throw new InvalidOperationException($"campo {nome} nao foi lido");

    [Fact]
    public void AchaALinhaDigitavelNoMeioDoTexto()
    {
        var texto = $"""
            Banco do Brasil
            Beneficiario: Energia SA
            {LinhaDigitavelTestes.Padrao}
            Pagavel em qualquer banco
            """;

        Assert.Equal(LinhaDigitavelTestes.Padrao, Campo(texto, NomeDoCampo.LinhaDigitavel).Valor);
    }

    /// <summary>
    /// A linha digitavel e achada mesmo sem a pontuacao que o banco imprime — leitura de imagem
    /// nem sempre preserva ponto e espaco, e exigi-los recusaria a maioria das leituras boas. O
    /// que sustenta a varredura e o verificador: janela errada nao fecha os quatro digitos.
    /// </summary>
    [Fact]
    public void AchaALinhaDigitavelPontuada()
    {
        const string texto = "00191.23454 67890.123457 67890.123457 9 15950000018990";

        Assert.Equal(LinhaDigitavelTestes.Padrao, Campo(texto, NomeDoCampo.LinhaDigitavel).Valor);
    }

    /// <summary>
    /// A confianca do campo vem da origem, e nao de quem leu. Linha digitavel que fecha os
    /// quatro digitos vale cheia — o modelo poderia ter dito 0,60 e nao mudaria nada.
    /// </summary>
    [Fact]
    public void CampoConferidoPorDigitoValeConfiancaCheia()
    {
        var linha = Campo(LinhaDigitavelTestes.Padrao, NomeDoCampo.LinhaDigitavel);

        Assert.Equal(1.00m, linha.Confianca);
        Assert.Equal(OrigemDoCampo.Estrutura, linha.Origem);
    }

    [Fact]
    public void TiraOValorEOVencimentoDaLinhaDigitavel()
    {
        var leitura = Ler(LinhaDigitavelTestes.Padrao);

        Assert.Equal("189.90", leitura.Campo(NomeDoCampo.Valor)!.Valor);
        Assert.Equal("2026-10-10", leitura.Campo(NomeDoCampo.Vencimento)!.Valor);
        Assert.Equal(1.00m, leitura.Confianca);
    }

    /// <summary>
    /// A assinatura do boleto adulterado: o valor impresso na folha nao e o do codigo de barras.
    /// A vitima le o valor certo e o banco cobra o do codigo.
    /// </summary>
    [Fact]
    public void ValorImpressoQueNaoBateComOCodigoDerrubaAConfianca()
    {
        var texto = $"""
            Valor do documento: 1.402,77
            {LinhaDigitavelTestes.Padrao}
            """;

        var valor = Campo(texto, NomeDoCampo.Valor);

        // O valor gravado continua sendo o do codigo, que e o que o banco cobraria.
        Assert.Equal("189.90", valor.Valor);
        Assert.Equal(0.20m, valor.Confianca);
        Assert.NotNull(valor.Observacao);
    }

    /// <summary>
    /// Multa e juros impressos ao lado do total nao sao divergencia: exigir que todos os valores
    /// da pagina batam com o codigo acusaria praticamente todo boleto.
    /// </summary>
    [Fact]
    public void ValorImpressoQueBateNaoEhDivergenciaMesmoComOutrosNumeros()
    {
        var texto = $"""
            Valor do documento: 189,90
            Multa: 3,80
            Juros por dia: 0,06
            {LinhaDigitavelTestes.Padrao}
            """;

        Assert.Equal(1.00m, Campo(texto, NomeDoCampo.Valor).Confianca);
    }

    /// <summary>
    /// Boleto de valor a combinar existe: o codigo vem zerado e o que sobra e o impresso, com a
    /// confianca que o impresso merece.
    /// </summary>
    [Fact]
    public void BoletoSemValorNoCodigoCaiParaOValorImpresso()
    {
        var texto = $"""
            Valor: 250,00
            {LinhaDigitavelTestes.SemValor}
            """;

        var valor = Campo(texto, NomeDoCampo.Valor);

        Assert.Equal("250.00", valor.Valor);
        Assert.Equal(OrigemDoCampo.Texto, valor.Origem);
        Assert.Equal(0.50m, valor.Confianca);
    }

    [Fact]
    public void BoletoAVistaTemVencimentoVazioComConfiancaCheia()
    {
        var vencimento = Campo(LinhaDigitavelTestes.SemVencimento, NomeDoCampo.Vencimento);

        Assert.Equal(string.Empty, vencimento.Valor);
        Assert.Equal(1.00m, vencimento.Confianca);
        Assert.Contains("vista", vencimento.Observacao!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fator que cabe nos dois ciclos de contagem sai com confianca baixa em vez de sair
    /// escolhido em silencio — quem decide um caso desses e gente.
    /// </summary>
    [Fact]
    public void VencimentoAmbiguoSaiComConfiancaBaixa()
    {
        const string ambigua = "00191234546789012345767890123457260000000005000";

        var vencimento = Campo(ambigua, NomeDoCampo.Vencimento);

        Assert.Equal(0.40m, vencimento.Confianca);
        Assert.Equal(0.40m, Ler(ambigua).Confianca);
    }

    /// <summary>
    /// A confianca do documento e o menor dos campos, e nao a media: media deixa um campo
    /// ilegivel passar escondido atras de tres perfeitos, e o ilegivel pode ser o valor.
    /// </summary>
    [Fact]
    public void AConfiancaDoDocumentoEhAMenorDosCampos()
    {
        var texto = $"""
            Valor do documento: 999,99
            {LinhaDigitavelTestes.Padrao}
            """;

        // Linha e vencimento valem 1,00; o valor divergente vale 0,20.
        Assert.Equal(0.20m, Ler(texto).Confianca);
    }

    /// <summary>
    /// Sem linha digitavel nao ha o que pagar, qualquer que seja o resto. Zero e nao um numero
    /// baixo: nao e "pouco confiavel", e "nao da".
    /// </summary>
    [Fact]
    public void TextoSemLinhaDigitavelNaoTemComoSerPago()
    {
        var leitura = Ler("Recibo de pagamento. Valor: 80,00. Vencimento 10/10/2026.");

        Assert.Equal(0m, leitura.Confianca);
        Assert.Null(leitura.Campo(NomeDoCampo.LinhaDigitavel));

        // O que deu para achar continua registrado, para quem for revisar na mao.
        Assert.Equal("80.00", leitura.Campo(NomeDoCampo.Valor)!.Valor);
        Assert.Equal("2026-10-10", leitura.Campo(NomeDoCampo.Vencimento)!.Valor);
    }

    [Fact]
    public void TextoVazioNaoProduzCampoNenhum()
    {
        Assert.Empty(Ler(string.Empty).Campos);
        Assert.Equal(0m, InterpretadorDeBoleto.Interpretar(null, Hoje).Confianca);
    }

    [Fact]
    public void AchaOCnpjPontuadoDoEmissor()
    {
        var texto = $"Beneficiario: Energia SA - CNPJ 11.222.333/0001-81\n{LinhaDigitavelTestes.Padrao}";

        var cnpj = Campo(texto, NomeDoCampo.CnpjDoEmissor);

        Assert.Equal(CnpjTestes.Valido, cnpj.Valor);
        Assert.Equal(0.90m, cnpj.Confianca);
    }

    /// <summary>
    /// Menos que confianca cheia porque o digito prova que aquilo <em>e</em> um CNPJ, nao que e
    /// o CNPJ do <em>emissor</em>: o boleto traz o do beneficiario e as vezes o do sacado.
    /// </summary>
    [Fact]
    public void DoisCnpjValidosNaPaginaDerrubamAConfianca()
    {
        var texto = $"""
            Beneficiario: 11.222.333/0001-81
            Sacado: 45.678.912/0001-55
            {LinhaDigitavelTestes.Padrao}
            """;

        Assert.Equal(0.45m, Campo(texto, NomeDoCampo.CnpjDoEmissor).Confianca);
    }

    /// <summary>
    /// O CNPJ nao entra na confianca do documento: ele importa para o registro, mas nao e ele
    /// que o banco usa para cobrar. Exigi-lo mandaria para a revisao todo boleto legivel cujo
    /// emissor nao imprimiu o CNPJ em posicao reconhecivel.
    /// </summary>
    [Fact]
    public void OCnpjAmbiguoNaoDerrubaAConfiancaDoDocumento()
    {
        var texto = $"""
            Beneficiario: 11.222.333/0001-81
            Sacado: 45.678.912/0001-55
            {LinhaDigitavelTestes.Padrao}
            """;

        Assert.Equal(1.00m, Ler(texto).Confianca);
    }

    /// <summary>
    /// Numero longo que nao fecha os verificadores nao vira CNPJ. Sem isso, o numero da nota
    /// fiscal viraria o CNPJ do emissor.
    /// </summary>
    [Fact]
    public void NumeroLongoQueNaoEhCnpjNaoViraCnpj()
    {
        var texto = $"Nota fiscal 99887766554433\n{LinhaDigitavelTestes.Padrao}";

        Assert.Null(Ler(texto).Campo(NomeDoCampo.CnpjDoEmissor));
    }

    /// <summary>
    /// A linha digitavel tem quatro digitos de conferencia e aguenta varredura de janela; o CNPJ
    /// tem dois, e uma janela em cento e vinte fecha por acaso. Por isso o CNPJ so e aceito
    /// pontuado ou como token isolado — dentro de uma sequencia maior, nao conta.
    /// </summary>
    [Fact]
    public void CnpjEnterradoDentroDeUmaSequenciaMaiorNaoConta()
    {
        var texto = $"Codigo 999{CnpjTestes.Valido}999\n{LinhaDigitavelTestes.Padrao}";

        Assert.Null(Ler(texto).Campo(NomeDoCampo.CnpjDoEmissor));
    }

    [Fact]
    public void AceitaCnpjAlfanumericoDoEmissor()
    {
        var texto = $"Beneficiario CNPJ 12.ABC.345/01DE-35\n{LinhaDigitavelTestes.Padrao}";

        Assert.Equal(CnpjTestes.Alfanumerico, Campo(texto, NomeDoCampo.CnpjDoEmissor).Valor);
    }

    /// <summary>
    /// Conta de concessionaria tem 48 algarismos e outro padrao. Ela nao vira boleto lido por
    /// acidente: nenhuma janela de 47 dentro dela fecha os quatro digitos.
    /// </summary>
    [Fact]
    public void ContaDeConcessionariaNaoViraBoleto()
    {
        var leitura = Ler("846700000017 39990010102 202600000001 70000123456");

        Assert.Null(leitura.Campo(NomeDoCampo.LinhaDigitavel));
        Assert.Equal(0m, leitura.Confianca);
    }

    /// <summary>
    /// A observacao existe para explicar a confianca, e nunca carrega conteudo do documento: e o
    /// tipo de campo que acaba em log e em tela de suporte.
    /// </summary>
    [Fact]
    public void AObservacaoNaoCarregaValorDoDocumento()
    {
        var texto = $"""
            Valor do documento: 1.402,77
            CNPJ 11.222.333/0001-81
            {LinhaDigitavelTestes.Padrao}
            """;

        foreach (var observacao in Ler(texto).Campos.Select(campo => campo.Observacao).OfType<string>())
        {
            Assert.DoesNotContain("1.402,77", observacao, StringComparison.Ordinal);
            Assert.DoesNotContain("11.222.333", observacao, StringComparison.Ordinal);
            Assert.DoesNotContain(LinhaDigitavelTestes.Padrao, observacao, StringComparison.Ordinal);
        }
    }
}
