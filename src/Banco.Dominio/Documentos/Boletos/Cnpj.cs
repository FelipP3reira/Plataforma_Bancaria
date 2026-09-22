using Banco.Dominio.Erros;

namespace Banco.Dominio.Documentos.Boletos;

/// <summary>
/// O CNPJ do emissor do documento, conferido pelos dois digitos verificadores.
/// </summary>
/// <remarks>
/// Conferir o digito e o que separa "o modelo leu um CNPJ" de "o modelo leu catorze
/// caracteres". Numa nota fiscal qualquer sequencia longa de algarismos parece CNPJ — numero
/// de nota, inscricao estadual, codigo de produto — e sem os verificadores o campo do emissor
/// seria preenchido com o primeiro numero grande que aparecesse na pagina.
/// <para>
/// Aceita a forma alfanumerica que passou a ser emitida em 2026: os doze primeiros caracteres
/// podem ser letra, e os dois ultimos continuam sendo algarismo. A conta e a mesma, com cada
/// caractere valendo o codigo ASCII menos 48 — o que faz <c>'0'</c> valer 0 e <c>'A'</c> valer
/// 17, mantendo os CNPJ antigos com o mesmo digito que sempre tiveram.
/// </para>
/// </remarks>
public readonly record struct Cnpj
{
    public const int Caracteres = 14;

    private const int CaracteresDaRaiz = 12;

    /// <summary>
    /// O deslocamento da tabela ASCII que a Receita escolheu para a versao alfanumerica.
    /// </summary>
    private const int DeslocamentoAscii = 48;

    private Cnpj(string texto) => Texto = texto;

    /// <summary>Os 14 caracteres em maiuscula, sem pontuacao.</summary>
    public string Texto { get; }

    public static Cnpj DoTexto(string? entrada)
    {
        if (!TentarLer(entrada, out var cnpj))
        {
            throw new BoletoInvalidoException("CNPJ invalido.");
        }

        return cnpj;
    }

    public static bool TentarLer(string? entrada, out Cnpj cnpj)
    {
        cnpj = default;

        if (string.IsNullOrWhiteSpace(entrada))
        {
            return false;
        }

        var limpo = Limpar(entrada);

        if (limpo.Length != Caracteres || !Formato(limpo))
        {
            return false;
        }

        var raiz = limpo.AsSpan(0, CaracteresDaRaiz);

        // Sequencia repetida passa na conta dos verificadores e nao existe na Receita. Sem
        // esta recusa, 00.000.000/0000-00 seria um CNPJ valido — e e justamente o que um
        // modelo devolve quando le um campo em branco.
        if (limpo.Distinct().Count() == 1)
        {
            return false;
        }

        var primeiro = Verificador(raiz);
        var segundo = Verificador(limpo.AsSpan(0, CaracteresDaRaiz + 1));

        if (limpo[12] != primeiro || limpo[13] != segundo)
        {
            return false;
        }

        cnpj = new Cnpj(limpo);

        return true;
    }

    /// <summary>A forma com pontuacao, para exibir.</summary>
    public string Formatado() =>
        $"{Texto[..2]}.{Texto[2..5]}.{Texto[5..8]}/{Texto[8..12]}-{Texto[12..]}";

    public override string ToString() => Texto;

    private static string Limpar(string entrada) =>
        new([.. entrada.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);

    /// <remarks>
    /// Os dois ultimos caracteres precisam ser algarismo: eles sao o resultado de uma conta, e
    /// o resultado de um modulo 11 nunca e letra.
    /// </remarks>
    private static bool Formato(string limpo) =>
        limpo.Take(CaracteresDaRaiz).All(char.IsAsciiLetterOrDigit)
        && char.IsAsciiDigit(limpo[12])
        && char.IsAsciiDigit(limpo[13]);

    /// <remarks>
    /// Modulo 11 com pesos de 2 a 9 girando da direita para a esquerda, igual ao digito geral
    /// do boleto — mas aqui resto 0 e 1 viram digito 0, e nao 1. Sao duas especificacoes
    /// diferentes que usam o mesmo modulo, e trocar uma pela outra faz os dois pararem de
    /// validar.
    /// </remarks>
    private static char Verificador(ReadOnlySpan<char> caracteres)
    {
        var soma = 0;
        var peso = 2;

        for (var posicao = caracteres.Length - 1; posicao >= 0; posicao--)
        {
            soma += (caracteres[posicao] - DeslocamentoAscii) * peso;

            peso = peso == 9 ? 2 : peso + 1;
        }

        var resto = soma % 11;

        return resto < 2 ? '0' : (char)('0' + (11 - resto));
    }
}
