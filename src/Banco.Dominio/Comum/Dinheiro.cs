using System.Globalization;
using Banco.Dominio.Erros;

namespace Banco.Dominio.Comum;

/// <summary>
/// Valor monetario em reais, com duas casas decimais e sem sinal.
/// </summary>
/// <remarks>
/// <c>decimal</c>, e nunca <c>double</c>: ponto flutuante binario nao representa 0,10
/// exatamente, e num ledger o erro nao se dilui — ele se acumula lancamento a lancamento
/// ate o saldo materializado divergir da soma da tabela.
/// <para>
/// Sem sinal de proposito. O que diz se o dinheiro entra ou sai e o tipo do lancamento, e
/// nao o sinal do valor: permitir credito negativo criaria dois jeitos de escrever um
/// debito, e um relatorio que somasse por tipo daria dois resultados diferentes.
/// </para>
/// </remarks>
public readonly record struct Dinheiro : IComparable<Dinheiro>
{
    public const int CasasDecimais = 2;

    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    private Dinheiro(decimal valor) => Valor = valor;

    public static Dinheiro Zero => new(0m);

    public decimal Valor { get; }

    public bool EhZero => Valor == 0m;

    public static Dinheiro De(decimal valor)
    {
        if (valor < 0m)
        {
            throw new ValorInvalidoException("Valor monetario nao pode ser negativo.");
        }

        // Arredondar em silencio esconderia entrada errada: um cliente que manda 10,999
        // acha que mandou dez reais e noventa e nove, e o extrato mostraria outra coisa.
        if (decimal.Round(valor, CasasDecimais) != valor)
        {
            throw new ValorInvalidoException(
                $"Valor monetario nao pode ter mais de {CasasDecimais} casas decimais.");
        }

        return new Dinheiro(valor);
    }

    public static Dinheiro operator +(Dinheiro esquerda, Dinheiro direita) =>
        new(esquerda.Valor + direita.Valor);

    /// <summary>
    /// Subtrai, recusando resultado negativo.
    /// </summary>
    /// <remarks>
    /// Saldo negativo nao existe neste sistema: nao ha cheque especial, e a unica forma de
    /// chegar la seria um debito passar sem conferencia. Fazer a propria subtracao recusar
    /// significa que nenhum caminho de codigo consegue produzir um saldo negativo, nem por
    /// esquecimento de quem escrever o proximo caso de uso.
    /// </remarks>
    public static Dinheiro operator -(Dinheiro esquerda, Dinheiro direita)
    {
        if (direita > esquerda)
        {
            throw new ValorInvalidoException("Operacao deixaria o valor negativo.");
        }

        return new Dinheiro(esquerda.Valor - direita.Valor);
    }

    public static bool operator <(Dinheiro esquerda, Dinheiro direita) => esquerda.Valor < direita.Valor;

    public static bool operator >(Dinheiro esquerda, Dinheiro direita) => esquerda.Valor > direita.Valor;

    public static bool operator <=(Dinheiro esquerda, Dinheiro direita) => esquerda.Valor <= direita.Valor;

    public static bool operator >=(Dinheiro esquerda, Dinheiro direita) => esquerda.Valor >= direita.Valor;

    public static Dinheiro Somar(Dinheiro esquerda, Dinheiro direita) => esquerda + direita;

    public static Dinheiro Subtrair(Dinheiro esquerda, Dinheiro direita) => esquerda - direita;

    public int CompareTo(Dinheiro outro) => Valor.CompareTo(outro.Valor);

    // Cultura fixa: o texto aparece em mensagem de erro e em log, e nao pode mudar de
    // formato porque o servidor subiu com outra configuracao regional.
    public override string ToString() => Valor.ToString("C2", Brasil);
}
