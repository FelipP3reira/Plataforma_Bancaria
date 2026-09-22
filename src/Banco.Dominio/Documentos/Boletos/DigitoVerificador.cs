namespace Banco.Dominio.Documentos.Boletos;

/// <summary>
/// Os dois digitos verificadores do boleto brasileiro.
/// </summary>
/// <remarks>
/// Sao eles que fazem a extracao ser verificavel em vez de acreditada. Um modelo de visao le
/// "8" onde estava "3" e devolve confianca alta do mesmo jeito, porque ele confia na propria
/// leitura. O digito nao: ele fecha ou nao fecha, e quando fecha em 47 digitos a chance de
/// ser uma leitura errada que por acaso fechou e baixa.
/// <para>
/// Sao dois algoritmos diferentes porque a FEBRABAN especificou dois: modulo 10 nos tres
/// campos da linha digitavel, modulo 11 no codigo de barras inteiro.
/// </para>
/// </remarks>
public static class DigitoVerificador
{
    /// <summary>
    /// Modulo 10 — o digito de cada um dos tres campos da linha digitavel.
    /// </summary>
    /// <remarks>
    /// Pesos alternando 2 e 1 da direita para a esquerda, e produto de dois algarismos tem os
    /// algarismos somados. E o mesmo algoritmo de cartao de credito: pega digito trocado
    /// sempre, e pega transposicao de vizinhos porque os dois vizinhos tem peso diferente.
    /// </remarks>
    public static char Modulo10(ReadOnlySpan<char> digitos)
    {
        var soma = 0;
        var peso = 2;

        for (var posicao = digitos.Length - 1; posicao >= 0; posicao--)
        {
            var produto = (digitos[posicao] - '0') * peso;

            // Subtrair 9 e o mesmo que somar os dois algarismos: 14 vira 5, e 1+4 tambem.
            soma += produto > 9 ? produto - 9 : produto;

            peso = peso == 2 ? 1 : 2;
        }

        return (char)('0' + ((10 - (soma % 10)) % 10));
    }

    /// <summary>
    /// Modulo 11 — o digito geral do codigo de barras, sobre os outros 43 algarismos.
    /// </summary>
    /// <remarks>
    /// Pesos de 2 a 9 girando da direita para a esquerda. Resto 0, 1 ou 10 vira digito 1 por
    /// especificacao: os tres casos cairiam fora da faixa de um algarismo, e a FEBRABAN
    /// escolheu colapsar os tres no mesmo valor em vez de proibir as chaves que os produzem.
    /// </remarks>
    public static char Modulo11(ReadOnlySpan<char> digitos)
    {
        var soma = 0;
        var peso = 2;

        for (var posicao = digitos.Length - 1; posicao >= 0; posicao--)
        {
            soma += (digitos[posicao] - '0') * peso;

            peso = peso == 9 ? 2 : peso + 1;
        }

        var resto = 11 - (soma % 11);

        return resto is 0 or 10 or 11 ? '1' : (char)('0' + resto);
    }
}
