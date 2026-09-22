namespace Banco.Dominio.Documentos.Boletos;

/// <summary>
/// O vencimento do boleto, que vem gravado como uma contagem de dias em quatro algarismos.
/// </summary>
/// <remarks>
/// Quatro algarismos dao 9000 datas contando de 1000, e o padrao nasceu em 1997 sem pensar no
/// que aconteceria quando acabassem. Acabaram: o fator 9999 caiu em <b>21/02/2025</b>, e a
/// FEBRABAN determinou que no dia seguinte o contador voltasse a 1000.
/// <para>
/// Isso significa que <b>o mesmo fator descreve duas datas</b> — 1000 e 03/07/2000 no ciclo
/// antigo e 22/02/2025 no novo. Ler o fator sem desambiguar devolveria vencimento de vinte e
/// cinco anos atras para um boleto emitido este mes, e um boleto vencido nao e um boleto
/// pagavel: a diferenca decide se o sistema cobra multa, recusa o pagamento ou paga na data.
/// </para>
/// </remarks>
public static class FatorDeVencimento
{
    /// <summary>Fator zero: boleto sem data de vencimento, pagavel a vista.</summary>
    public const int SemVencimento = 0;

    public const int Minimo = 1000;
    public const int Maximo = 9999;

    private static readonly DateOnly BaseDoCicloAntigo = new(1997, 10, 7);

    /// <summary>
    /// A base que faz o fator 1000 cair em 22/02/2025, o dia em que o contador reiniciou.
    /// </summary>
    private static readonly DateOnly BaseDoCicloNovo = new(2022, 5, 29);

    /// <summary>
    /// Quanto um vencimento pode se afastar de hoje e ainda ser uma data crivel.
    /// </summary>
    /// <remarks>
    /// Boleto e instrumento de curto prazo: emissao e vencimento ficam a meses de distancia,
    /// nao a decadas. A janela e o que permite escolher entre os dois ciclos — e ela e larga
    /// para os dois lados porque boleto vencido continua existindo e sendo pago com multa.
    /// </remarks>
    private static readonly TimeSpan JanelaCrivel = TimeSpan.FromDays(5 * 365);

    /// <summary>
    /// Traduz o fator em data, escolhendo o ciclo pela unica pista que existe: qual das duas
    /// leituras e crivel para quem esta lendo agora.
    /// </summary>
    /// <returns>
    /// A data, ou nulo quando o fator e zero. O <paramref name="ambiguo"/> sai verdadeiro
    /// quando nenhuma das duas leituras e crivel e a escolha foi arbitraria.
    /// </returns>
    public static DateOnly? Resolver(int fator, DateOnly hoje, out bool ambiguo)
    {
        ambiguo = false;

        if (fator == SemVencimento)
        {
            return null;
        }

        if (fator is < Minimo or > Maximo)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fator),
                fator,
                $"Fator de vencimento fora da faixa de {Minimo} a {Maximo}.");
        }

        var novo = BaseDoCicloNovo.AddDays(fator);
        var antigo = BaseDoCicloAntigo.AddDays(fator);

        var novoCrivel = Crivel(novo, hoje);
        var antigoCrivel = Crivel(antigo, hoje);

        if (novoCrivel != antigoCrivel)
        {
            return novoCrivel ? novo : antigo;
        }

        // Nenhuma das duas serve, ou as duas servem. Fica com o ciclo em vigor e avisa: o
        // acerto silencioso e o que produz boleto pago na data errada, e quem decide um caso
        // desses e gente, na revisao.
        ambiguo = true;

        return novo;
    }

    private static bool Crivel(DateOnly data, DateOnly hoje)
    {
        var distancia = data.ToDateTime(TimeOnly.MinValue) - hoje.ToDateTime(TimeOnly.MinValue);

        return distancia.Duration() <= JanelaCrivel;
    }
}
