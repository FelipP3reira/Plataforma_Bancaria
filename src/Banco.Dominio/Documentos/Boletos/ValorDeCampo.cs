using System.Globalization;
using Banco.Dominio.Comum;
using Banco.Dominio.Erros;

namespace Banco.Dominio.Documentos.Boletos;

/// <summary>
/// Poe o valor de um campo na forma canonica, recusando o que nao serve para aquele campo.
/// </summary>
/// <remarks>
/// Existe por causa da correcao humana. O valor que a maquina leu ja nasce canonico; o que
/// alguem digita na tela de revisao nao — e a correcao e justamente o caminho por onde um
/// valor arbitrario entra no sistema, sem passar pelo verificador que barrou a leitura
/// automatica.
/// <para>
/// Sem esta conferencia, corrigir a linha digitavel para qualquer coisa fecharia o documento
/// como revisado e o pagamento cobraria o que foi digitado. A revisao existe para <b>consertar</b>
/// o que a maquina errou, nao para contornar a conferencia.
/// </para>
/// </remarks>
public static class ValorDeCampo
{
    public static string Canonizar(NomeDoCampo nome, string? valor)
    {
        var limpo = valor?.Trim() ?? string.Empty;

        return nome switch
        {
            NomeDoCampo.LinhaDigitavel => LinhaDigitavel.DoTexto(limpo).Digitos,
            NomeDoCampo.CnpjDoEmissor => Cnpj.DoTexto(limpo).Texto,
            NomeDoCampo.Valor => ComoValor(limpo),
            NomeDoCampo.Vencimento => ComoData(limpo),
            _ => throw new BoletoInvalidoException($"Campo desconhecido: {nome}."),
        };
    }

    /// <remarks>
    /// Aceita tanto a forma canonica quanto a brasileira: quem corrige na tela digita
    /// <c>1.402,77</c>, e recusar por causa da virgula seria rigor contra a pessoa errada.
    /// </remarks>
    private static string ComoValor(string entrada)
    {
        var bruto = entrada.Contains(',', StringComparison.Ordinal)
            ? entrada.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
            : entrada;

        if (!decimal.TryParse(bruto, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor))
        {
            throw new BoletoInvalidoException($"Valor invalido: '{entrada}'.");
        }

        // Dinheiro e quem recusa negativo e casa decimal sobrando. A conversao passa por ele de
        // proposito: um campo de valor que aceitasse o que o ledger recusa produziria documento
        // revisado que nao da para pagar.
        return Dinheiro.De(valor).Valor.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string ComoData(string entrada)
    {
        // Vazio e legitimo: boleto a vista nao tem vencimento.
        if (entrada.Length == 0)
        {
            return string.Empty;
        }

        string[] formatos = ["yyyy-MM-dd", "dd/MM/yyyy"];

        if (!DateOnly.TryParseExact(entrada, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
        {
            throw new BoletoInvalidoException($"Data invalida: '{entrada}'. Use aaaa-mm-dd ou dd/mm/aaaa.");
        }

        return data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
