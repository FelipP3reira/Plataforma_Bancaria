using Banco.Dominio.Comum;
using Banco.Dominio.Erros;

namespace Banco.Dominio.Documentos.Boletos;

/// <summary>
/// A linha digitavel de um boleto de cobranca bancaria: 47 algarismos que carregam banco,
/// moeda, vencimento, valor e quatro digitos verificadores.
/// </summary>
/// <remarks>
/// Este e o tipo que faz a extracao deixar de ser um palpite. Os quatro verificadores fecham
/// ou nao fecham, e quando fecham em 47 algarismos a leitura errada teria que ter acertado
/// quatro contas por acaso — e e por isso que a confianca de um campo lido daqui nao depende
/// do quanto o modelo diz confiar em si mesmo.
/// <para>
/// Os campos nao estao onde a intuicao coloca: o valor mora nas posicoes 38 a 47 da linha
/// digitavel, mas nas posicoes 10 a 19 do codigo de barras, porque a linha digitavel e uma
/// <b>reordenacao</b> do codigo de barras com digitos de conferencia intercalados. Reconstruir
/// o codigo de barras e o que permite conferir o digito geral.
/// </para>
/// </remarks>
public readonly record struct LinhaDigitavel
{
    public const int Algarismos = 47;

    /// <summary>Quantos algarismos tem a linha digitavel de conta de concessionaria.</summary>
    public const int AlgarismosDeConcessionaria = 48;

    public const int AlgarismosDoCodigoDeBarras = 44;

    private LinhaDigitavel(string digitos, string codigoDeBarras)
    {
        Digitos = digitos;
        CodigoDeBarras = codigoDeBarras;
    }

    /// <summary>Os 47 algarismos, sem pontuacao.</summary>
    public string Digitos { get; }

    /// <summary>Os 44 algarismos do codigo de barras, remontados a partir da linha.</summary>
    public string CodigoDeBarras { get; }

    public string Banco => Digitos[..3];

    /// <summary>Codigo da moeda. 9 e real; qualquer outra coisa nao se paga aqui.</summary>
    public char Moeda => Digitos[3];

    public int Fator => int.Parse(Digitos.AsSpan(33, 4), provider: null);

    /// <summary>
    /// O valor, lido dos dez algarismos finais como centavos.
    /// </summary>
    /// <remarks>
    /// Centavos, e nao reais com ponto: o boleto grava <c>0000018990</c> para R$ 189,90, e
    /// dividir por cem no fim e a unica conversao. Ler como reais exigiria inserir uma virgula
    /// numa posicao fixa, que e a mesma coisa escrita de um jeito que erra mais.
    /// </remarks>
    public long ValorEmCentavos => long.Parse(Digitos.AsSpan(37, 10), provider: null);

    /// <summary>Valor zero significa boleto sem valor impresso, a ser combinado.</summary>
    public Dinheiro? Valor =>
        ValorEmCentavos == 0 ? null : Dinheiro.De(ValorEmCentavos / 100m);

    /// <summary>Le e confere, ou diz por que nao serve.</summary>
    public static LinhaDigitavel DoTexto(string? entrada)
    {
        var digitos = SoOsAlgarismos(entrada);

        // A conta de luz, de agua e de tributo tem 48 algarismos e outro padrao inteiro:
        // outra posicao de valor, outro verificador, e o vencimento nem sempre esta la. Um
        // leitor que aceitasse os dois e errasse a escolha pagaria o valor lido do lugar
        // errado — recusar e dizer o que e e mais honesto do que adivinhar.
        if (digitos.Length == AlgarismosDeConcessionaria)
        {
            throw new BoletoInvalidoException(
                "Linha de 48 algarismos e conta de concessionaria, que tem outro padrao. "
                + "Este leitor entende boleto de cobranca bancaria.");
        }

        if (digitos.Length != Algarismos)
        {
            throw new BoletoInvalidoException(
                $"Linha digitavel tem {digitos.Length} algarismos; o esperado e {Algarismos}.");
        }

        ConferirCampos(digitos);

        var codigoDeBarras = RemontarCodigoDeBarras(digitos);

        ConferirDigitoGeral(codigoDeBarras);

        return new LinhaDigitavel(digitos, codigoDeBarras);
    }

    public static bool TentarLer(string? entrada, out LinhaDigitavel linha)
    {
        try
        {
            linha = DoTexto(entrada);

            return true;
        }
        catch (BoletoInvalidoException)
        {
            linha = default;

            return false;
        }
    }

    public override string ToString() => Digitos;

    /// <remarks>
    /// Aceita com ou sem a pontuacao que os bancos imprimem: quem digita copia com pontos e
    /// espacos, e o leitor de texto extrai com o que estava na imagem. O que importa sao os
    /// algarismos e a ordem deles.
    /// </remarks>
    private static string SoOsAlgarismos(string? entrada) =>
        string.IsNullOrWhiteSpace(entrada)
            ? string.Empty
            : new string([.. entrada.Where(char.IsAsciiDigit)]);

    /// <remarks>
    /// Os tres campos tem o verificador imediatamente depois dos seus proprios algarismos, e
    /// cada um se confere sozinho. E o que permite dizer <em>onde</em> a leitura errou, em vez
    /// de so dizer que a linha inteira nao fecha.
    /// </remarks>
    private static void ConferirCampos(string digitos)
    {
        ConferirCampo(digitos, inicio: 0, tamanho: 9, campo: 1);
        ConferirCampo(digitos, inicio: 10, tamanho: 10, campo: 2);
        ConferirCampo(digitos, inicio: 21, tamanho: 10, campo: 3);
    }

    private static void ConferirCampo(string digitos, int inicio, int tamanho, int campo)
    {
        var esperado = DigitoVerificador.Modulo10(digitos.AsSpan(inicio, tamanho));
        var encontrado = digitos[inicio + tamanho];

        if (esperado != encontrado)
        {
            throw new BoletoInvalidoException(
                $"Digito verificador do campo {campo} nao fecha: esperado {esperado}, lido {encontrado}.");
        }
    }

    /// <remarks>
    /// A linha digitavel embaralha o codigo de barras de proposito, para que os campos livres
    /// fiquem em blocos de tamanho digitavel. Desembaralhar e so remontar na ordem original.
    /// </remarks>
    private static string RemontarCodigoDeBarras(string linha)
    {
        Span<char> barras = stackalloc char[AlgarismosDoCodigoDeBarras];

        linha.AsSpan(0, 4).CopyTo(barras);           // banco e moeda
        linha.AsSpan(32, 1).CopyTo(barras[4..]);     // o digito geral
        linha.AsSpan(33, 14).CopyTo(barras[5..]);    // fator de vencimento e valor
        linha.AsSpan(4, 5).CopyTo(barras[19..]);     // campo livre, primeiro bloco
        linha.AsSpan(10, 10).CopyTo(barras[24..]);   // segundo bloco
        linha.AsSpan(21, 10).CopyTo(barras[34..]);   // terceiro bloco

        return new string(barras);
    }

    private static void ConferirDigitoGeral(string codigoDeBarras)
    {
        // O proprio digito sai da conta: ele esta na posicao 5 e o modulo 11 roda sobre os
        // outros 43 algarismos.
        var semODigito = string.Concat(codigoDeBarras.AsSpan(0, 4), codigoDeBarras.AsSpan(5));

        var esperado = DigitoVerificador.Modulo11(semODigito);
        var encontrado = codigoDeBarras[4];

        if (esperado != encontrado)
        {
            throw new BoletoInvalidoException(
                $"Digito verificador geral nao fecha: esperado {esperado}, lido {encontrado}.");
        }
    }
}
