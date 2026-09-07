using System.Globalization;
using Banco.Dominio.Erros;

namespace Banco.Dominio.Contas;

/// <summary>
/// Numero de conta no formato <c>NNNNNNN-D</c>, com digito verificador modulo 11.
/// </summary>
/// <remarks>
/// O digito nao e enfeite: numero de conta e digitado a mao em transferencia, e um digito
/// trocado sem verificacao acha outra conta que existe e manda dinheiro para o desconhecido
/// certo. Com o verificador, quase todo erro de digitacao vira "conta invalida" antes de
/// virar consulta ao banco.
/// <para>
/// A parte sequencial vem de fora — de uma sequencia do banco de dados. O dominio nao
/// inventa numero: se inventasse, dois processos poderiam inventar o mesmo.
/// </para>
/// </remarks>
public sealed record NumeroDaConta
{
    public const int DigitosDaSequencia = 7;

    private NumeroDaConta(string texto) => Texto = texto;

    public string Texto { get; }

    /// <summary>Monta o numero a partir do valor cru da sequencia.</summary>
    public static NumeroDaConta DaSequencia(long sequencia)
    {
        if (sequencia is < 1 or > 9_999_999)
        {
            throw new ContaInvalidaException(
                $"Sequencia de conta fora da faixa de {DigitosDaSequencia} digitos: {sequencia}.");
        }

        var digitos = sequencia.ToString(CultureInfo.InvariantCulture).PadLeft(DigitosDaSequencia, '0');

        return new NumeroDaConta($"{digitos}-{Verificador(digitos)}");
    }

    public static NumeroDaConta Criar(string? entrada)
    {
        if (!TentarCriar(entrada, out var numero))
        {
            throw new ContaInvalidaException("Numero de conta invalido.");
        }

        return numero;
    }

    public static bool TentarCriar(string? entrada, out NumeroDaConta numero)
    {
        numero = null!;

        if (string.IsNullOrWhiteSpace(entrada))
        {
            return false;
        }

        // Aceita com ou sem o hifen: quem digita costuma omitir, e recusar por causa da
        // pontuacao seria rigor sem ganho — o que importa e o digito bater.
        var limpo = entrada.Replace("-", string.Empty, StringComparison.Ordinal).Trim();

        if (limpo.Length != DigitosDaSequencia + 1 || !limpo.All(char.IsAsciiDigit))
        {
            return false;
        }

        var digitos = limpo[..DigitosDaSequencia];
        if (Verificador(digitos) != limpo[^1])
        {
            return false;
        }

        numero = new NumeroDaConta($"{digitos}-{limpo[^1]}");
        return true;
    }

    private static char Verificador(string digitos)
    {
        var soma = 0;
        var peso = 2;

        // Pesos crescentes da direita para a esquerda: e o que faz a troca de dois digitos
        // vizinhos mudar a soma. Peso fixo nao pegaria transposicao, que e o erro de
        // digitacao mais comum depois do digito trocado.
        for (var posicao = digitos.Length - 1; posicao >= 0; posicao--)
        {
            soma += (digitos[posicao] - '0') * peso++;
        }

        var resto = 11 - (soma % 11);
        return resto >= 10 ? '0' : (char)('0' + resto);
    }

    public override string ToString() => Texto;
}
