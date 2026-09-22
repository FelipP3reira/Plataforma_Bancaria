using System.Globalization;
using System.Text.RegularExpressions;

namespace Banco.Dominio.Documentos.Boletos;

/// <summary>
/// Transforma o texto lido de um documento nos campos que se pode pagar.
/// </summary>
/// <remarks>
/// A ideia que organiza esta classe: <b>a estrutura confere o modelo, e nao o contrario</b>. Um
/// modelo de visao devolve a propria confianca, e essa confianca e uma opiniao — ele diz 0,97
/// tendo lido "8" onde estava "3", porque ele nao tem como saber. A linha digitavel tem quatro
/// digitos verificadores: se os quatro fecham, a leitura esta certa quase com certeza, e a
/// opiniao do modelo sobre si mesmo deixa de importar. Se nao fecham, nenhuma confianca
/// declarada salva.
/// <para>
/// Por isso cada campo sai daqui com a confianca <em>derivada da origem</em>, e nao herdada de
/// quem leu: o que veio conferido por digito vale mais do que o que veio por formato.
/// </para>
/// </remarks>
public static partial class InterpretadorDeBoleto
{
    /// <summary>Campo que se confere sozinho: a leitura errada teria que acertar as contas.</summary>
    private const decimal ConfiancaDeEstrutura = 1.00m;

    /// <summary>
    /// CNPJ com os verificadores batendo, mas em posicao nao confirmada.
    /// </summary>
    /// <remarks>
    /// Menos que cheia porque o digito prova que aquilo <em>e</em> um CNPJ, nao que e o CNPJ do
    /// <em>emissor</em>. Um boleto traz o do beneficiario e as vezes o do sacado, e os dois
    /// passam na mesma conta.
    /// </remarks>
    private const decimal ConfiancaDeCnpjUnico = 0.90m;

    /// <summary>Mais de um CNPJ valido na pagina: da para ler, nao da para escolher.</summary>
    private const decimal ConfiancaDeCnpjAmbiguo = 0.45m;

    /// <summary>Valor ou data achados por formato, sem nada confirmando.</summary>
    private const decimal ConfiancaDeTexto = 0.50m;

    /// <summary>
    /// O fator de vencimento cai nos dois ciclos de contagem e nenhuma das datas e crivel.
    /// </summary>
    private const decimal ConfiancaDeVencimentoAmbiguo = 0.40m;

    /// <summary>
    /// A estrutura diz um valor e o texto impresso diz outro.
    /// </summary>
    /// <remarks>
    /// Abaixo de qualquer limiar razoavel de proposito. Divergencia entre o codigo de barras e
    /// o valor impresso e o formato classico de boleto adulterado: a vitima le o valor certo na
    /// folha e o banco cobra o do codigo. Isto nao se resolve com heuristica — vai para a mao
    /// de alguem.
    /// </remarks>
    private const decimal ConfiancaDeValorDivergente = 0.20m;

    public static LeituraDoDocumento Interpretar(string? texto, DateOnly hoje)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return LeituraDoDocumento.Nada;
        }

        var campos = new List<CampoLido>();

        var linha = AcharLinhaDigitavel(texto);

        if (linha is { } encontrada)
        {
            campos.Add(new CampoLido(
                NomeDoCampo.LinhaDigitavel,
                encontrada.Digitos,
                ConfiancaDeEstrutura,
                OrigemDoCampo.Estrutura));

            campos.Add(CampoDeValor(encontrada, texto));
            campos.Add(CampoDeVencimento(encontrada, hoje));
        }
        else
        {
            AdicionarSeAchar(campos, ValorNoTexto(texto));
            AdicionarSeAchar(campos, VencimentoNoTexto(texto));
        }

        AdicionarSeAchar(campos, CampoDeCnpj(texto));

        return new LeituraDoDocumento(campos, ConfiancaDoConjunto(campos, linha is not null));
    }

    private static void AdicionarSeAchar(List<CampoLido> campos, CampoLido? campo)
    {
        if (campo is not null)
        {
            campos.Add(campo);
        }
    }

    /// <summary>
    /// A confianca do documento: o menor valor entre os campos sem os quais nao se paga.
    /// </summary>
    /// <remarks>
    /// O CNPJ fica fora da conta. Ele importa para o registro e para conferir a quem se esta
    /// pagando, mas nao e ele que o banco usa para cobrar — exigi-lo mandaria para a revisao
    /// manual todo boleto legivel cujo emissor por acaso nao imprimiu o CNPJ em posicao
    /// reconhecivel.
    /// </remarks>
    private static decimal ConfiancaDoConjunto(List<CampoLido> campos, bool temLinha)
    {
        if (!temLinha)
        {
            // Sem linha digitavel nao ha o que pagar, qualquer que seja o resto. Zero em vez
            // de um numero baixo porque nao e "pouco confiavel": e "nao da".
            return 0m;
        }

        return campos
            .Where(campo => campo.Nome is NomeDoCampo.LinhaDigitavel or NomeDoCampo.Valor or NomeDoCampo.Vencimento)
            .Min(campo => campo.Confianca);
    }

    /// <remarks>
    /// Varre janelas de 47 algarismos em vez de procurar um padrao pontuado. O texto vem de
    /// leitura de imagem: o ponto e o espaco que o banco imprime podem nao ter sobrevivido, e
    /// exigi-los recusaria a maioria das leituras boas. O que sustenta a varredura e o
    /// verificador — janela errada nao fecha os quatro digitos.
    /// </remarks>
    private static LinhaDigitavel? AcharLinhaDigitavel(string texto)
    {
        foreach (var bloco in BlocosDeAlgarismos(texto))
        {
            for (var inicio = 0; inicio + LinhaDigitavel.Algarismos <= bloco.Length; inicio++)
            {
                if (LinhaDigitavel.TentarLer(bloco.Substring(inicio, LinhaDigitavel.Algarismos), out var linha))
                {
                    return linha;
                }
            }
        }

        return null;
    }

    /// <remarks>
    /// Linha por linha primeiro, e o texto inteiro depois: a linha digitavel costuma sair numa
    /// linha so, e comecar por ela evita emendar o fim de um numero com o comeco do seguinte. O
    /// texto inteiro entra como segunda tentativa para o caso de a leitura ter quebrado a linha
    /// no meio.
    /// </remarks>
    private static IEnumerable<string> BlocosDeAlgarismos(string texto)
    {
        foreach (var linha in texto.Split('\n'))
        {
            var algarismos = SoOsAlgarismos(linha);

            if (algarismos.Length >= LinhaDigitavel.Algarismos)
            {
                yield return algarismos;
            }
        }

        var tudo = SoOsAlgarismos(texto);

        if (tudo.Length >= LinhaDigitavel.Algarismos)
        {
            yield return tudo;
        }
    }

    private static string SoOsAlgarismos(string entrada) =>
        new([.. entrada.Where(char.IsAsciiDigit)]);

    /// <remarks>
    /// O valor da estrutura vale mais, mas o valor impresso e conferido contra ele. Boleto com
    /// codigo de barras adulterado tem exatamente esta assinatura: os dois numeros nao batem.
    /// </remarks>
    private static CampoLido CampoDeValor(LinhaDigitavel linha, string texto)
    {
        if (linha.Valor is not { } valor)
        {
            // Valor zerado no codigo e legitimo: boleto de valor a combinar existe. Ai o que
            // sobra e o impresso, com a confianca que o impresso merece.
            return ValorNoTexto(texto)
                ?? new CampoLido(
                    NomeDoCampo.Valor,
                    "0.00",
                    ConfiancaDeTexto,
                    OrigemDoCampo.Estrutura,
                    "Boleto sem valor no codigo de barras.");
        }

        var canonico = valor.Valor.ToString("0.00", CultureInfo.InvariantCulture);
        var impressos = ValoresImpressos(texto);

        // So acusa divergencia quando o texto tem valores e nenhum deles e o da estrutura.
        // Exigir que todos batam acusaria qualquer boleto que imprima multa ou juros ao lado.
        var divergente = impressos.Count > 0 && !impressos.Contains(valor.Valor);

        return divergente
            ? new CampoLido(
                NomeDoCampo.Valor,
                canonico,
                ConfiancaDeValorDivergente,
                OrigemDoCampo.Estrutura,
                "O valor impresso no documento nao aparece no codigo de barras.")
            : new CampoLido(NomeDoCampo.Valor, canonico, ConfiancaDeEstrutura, OrigemDoCampo.Estrutura);
    }

    private static CampoLido CampoDeVencimento(LinhaDigitavel linha, DateOnly hoje)
    {
        var data = FatorDeVencimento.Resolver(linha.Fator, hoje, out var ambiguo);

        if (data is not { } vencimento)
        {
            return new CampoLido(
                NomeDoCampo.Vencimento,
                string.Empty,
                ConfiancaDeEstrutura,
                OrigemDoCampo.Estrutura,
                "Boleto sem data de vencimento, pagavel a vista.");
        }

        return new CampoLido(
            NomeDoCampo.Vencimento,
            vencimento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ambiguo ? ConfiancaDeVencimentoAmbiguo : ConfiancaDeEstrutura,
            OrigemDoCampo.Estrutura,
            ambiguo ? "O fator de vencimento cabe em dois ciclos de contagem." : null);
    }

    private static CampoLido? ValorNoTexto(string texto)
    {
        var valores = ValoresImpressos(texto);

        if (valores.Count == 0)
        {
            return null;
        }

        // O maior, quando ha varios: num boleto os numeros vizinhos do valor sao multa, juros e
        // desconto, e todos sao menores que o total. E um palpite, e a confianca diz isso.
        return new CampoLido(
            NomeDoCampo.Valor,
            valores.Max().ToString("0.00", CultureInfo.InvariantCulture),
            ConfiancaDeTexto,
            OrigemDoCampo.Texto,
            valores.Count > 1 ? "Mais de um valor impresso no documento." : null);
    }

    private static HashSet<decimal> ValoresImpressos(string texto)
    {
        var valores = new HashSet<decimal>();

        foreach (var achado in ValorEmReais().Matches(texto).Cast<Match>())
        {
            var bruto = achado.Groups["valor"].Value
                .Replace(".", string.Empty, StringComparison.Ordinal)
                .Replace(',', '.');

            if (decimal.TryParse(bruto, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor))
            {
                valores.Add(valor);
            }
        }

        return valores;
    }

    private static CampoLido? VencimentoNoTexto(string texto)
    {
        var datas = DataNoTexto().Matches(texto)
            .Cast<Match>()
            .Select(achado => DateOnly.TryParseExact(
                achado.Value,
                "dd/MM/yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var data)
                ? data
                : (DateOnly?)null)
            .OfType<DateOnly>()
            .Distinct()
            .ToList();

        if (datas.Count == 0)
        {
            return null;
        }

        return new CampoLido(
            NomeDoCampo.Vencimento,
            datas.Max().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ConfiancaDeTexto,
            OrigemDoCampo.Texto,
            datas.Count > 1 ? "Mais de uma data impressa no documento." : null);
    }

    /// <remarks>
    /// So candidato pontuado ou token isolado de catorze caracteres. Varrer janelas como se faz
    /// com a linha digitavel nao serve aqui: os dois verificadores do CNPJ deixam passar mais ou
    /// menos uma janela em cento e vinte por acaso, e numa pagina de nota fiscal isso produz
    /// varios CNPJ inventados. A linha digitavel aguenta a varredura porque tem quatro digitos
    /// de conferencia, nao dois.
    /// </remarks>
    private static CampoLido? CampoDeCnpj(string texto)
    {
        var achados = CandidatoDeCnpj().Matches(texto)
            .Cast<Match>()
            .Select(achado => Cnpj.TentarLer(achado.Value, out var cnpj) ? cnpj.Texto : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (achados.Count == 0)
        {
            return null;
        }

        return new CampoLido(
            NomeDoCampo.CnpjDoEmissor,
            achados[0],
            achados.Count == 1 ? ConfiancaDeCnpjUnico : ConfiancaDeCnpjAmbiguo,
            OrigemDoCampo.Estrutura,
            achados.Count == 1
                ? "Os verificadores fecham; a posicao no documento nao foi confirmada."
                : "Mais de um CNPJ valido no documento.");
    }

    /// <remarks>
    /// A olhada para tras recusa algarismo e ponto, para que <c>1.402,77</c> nao case tambem
    /// como <c>402,77</c>. A olhada para frente recusa algarismo, e ponto ou virgula
    /// <em>seguidos de algarismo</em> — recusar o ponto sozinho perderia todo valor no fim de
    /// frase, que e onde ele costuma aparecer em texto corrido.
    /// </remarks>
    [GeneratedRegex(@"(?<![\d,.])(?<valor>\d{1,3}(?:\.\d{3})*,\d{2})(?!\d|[.,]\d)")]
    private static partial Regex ValorEmReais();

    [GeneratedRegex(@"(?<!\d)\d{2}/\d{2}/\d{4}(?!\d)")]
    private static partial Regex DataNoTexto();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:[A-Za-z0-9]{2}\.[A-Za-z0-9]{3}\.[A-Za-z0-9]{3}/[A-Za-z0-9]{4}-\d{2}|[A-Za-z0-9]{12}\d{2})(?![A-Za-z0-9])")]
    private static partial Regex CandidatoDeCnpj();
}
