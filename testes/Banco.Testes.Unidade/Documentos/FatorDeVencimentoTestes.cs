using Banco.Dominio.Documentos.Boletos;

namespace Banco.Testes.Unidade.Documentos;

/// <summary>
/// O vencimento gravado em quatro algarismos, e a virada de 2025 que fez o mesmo fator
/// descrever duas datas.
/// </summary>
public class FatorDeVencimentoTestes
{
    private static readonly DateOnly Hoje = new(2026, 9, 22);

    private static DateOnly? Resolver(int fator, DateOnly? hoje = null) =>
        FatorDeVencimento.Resolver(fator, hoje ?? Hoje, out _);

    private static bool Ambiguo(int fator, DateOnly? hoje = null)
    {
        FatorDeVencimento.Resolver(fator, hoje ?? Hoje, out var ambiguo);

        return ambiguo;
    }

    [Fact]
    public void FatorZeroEhBoletoSemVencimento() => Assert.Null(Resolver(FatorDeVencimento.SemVencimento));

    /// <summary>
    /// O dia em que o contador reiniciou. O fator 9999 caiu em 21/02/2025 e a FEBRABAN
    /// determinou que no dia seguinte voltasse a 1000.
    /// </summary>
    [Fact]
    public void OFator1000ApontaParaODiaDaVirada() =>
        Assert.Equal(new DateOnly(2025, 2, 22), Resolver(1000));

    [Fact]
    public void OFatorDoBoletoPadraoApontaParaOVencimentoDele() =>
        Assert.Equal(new DateOnly(2026, 10, 10), Resolver(1595));

    /// <summary>
    /// Fator alto ainda pertence ao ciclo antigo: 9999 era 21/02/2025, e lido no ciclo novo
    /// cairia em 2049. Escolher o ciclo errado aqui devolveria vencimento de vinte e tres anos
    /// no futuro para um boleto que venceu ano passado.
    /// </summary>
    [Fact]
    public void FatorAltoPertenceAoCicloAntigo()
    {
        Assert.Equal(new DateOnly(2025, 2, 21), Resolver(9999));
        Assert.Equal(new DateOnly(2025, 1, 15), Resolver(9962));
        Assert.False(Ambiguo(9999));
    }

    /// <summary>
    /// A leitura do ciclo novo ganha quando a do antigo ficou velha demais para ser crivel.
    /// </summary>
    [Fact]
    public void FatorBaixoPertenceAoCicloNovo()
    {
        // 1595 no ciclo antigo seria 2002; boleto emitido agora nao vence em 2002.
        Assert.Equal(new DateOnly(2026, 10, 10), Resolver(1595));
        Assert.False(Ambiguo(1595));
    }

    /// <summary>
    /// Quando nenhuma das duas datas e crivel, o fator e ambiguo e a leitura diz isso em vez de
    /// escolher em silencio. O acerto silencioso e o que produz boleto pago na data errada.
    /// </summary>
    [Fact]
    public void FatorNoMeioNaoDaParaDesambiguar()
    {
        Assert.True(Ambiguo(6000));

        // Fica com o ciclo em vigor, que e a aposta menos ruim — mas marcado.
        Assert.Equal(new DateOnly(2038, 11, 1), Resolver(6000));
    }

    /// <summary>
    /// O mesmo fator, lido em 2020, resolvia para o ciclo antigo. A desambiguacao depende de
    /// quem esta lendo e quando — nao existe resposta independente da data de leitura.
    /// </summary>
    [Fact]
    public void OMesmoFatorMudaDeCicloConformeAEpocaDaLeitura()
    {
        Assert.Equal(new DateOnly(2014, 3, 12), Resolver(6000, new DateOnly(2015, 1, 1)));
        Assert.Equal(new DateOnly(2038, 11, 1), Resolver(6000, new DateOnly(2036, 1, 1)));
    }

    /// <summary>Boleto vencido continua existindo e sendo pago com multa.</summary>
    [Fact]
    public void VencimentoNoPassadoRecenteEhCrivel() =>
        Assert.Equal(new DateOnly(2025, 2, 22), Resolver(1000, new DateOnly(2026, 9, 22)));

    [Theory]
    [InlineData(1)]
    [InlineData(999)]
    [InlineData(10000)]
    [InlineData(-1)]
    public void FatorForaDaFaixaEhRecusado(int fator) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Resolver(fator));
}
