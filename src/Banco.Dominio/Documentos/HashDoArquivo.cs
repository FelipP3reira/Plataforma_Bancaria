using System.Globalization;
using System.Security.Cryptography;
using Banco.Dominio.Erros;

namespace Banco.Dominio.Documentos;

/// <summary>
/// O SHA-256 do conteudo, em minusculas.
/// </summary>
/// <remarks>
/// E a identidade do documento para efeito de idempotencia: o mesmo boleto enviado duas
/// vezes tem o mesmo hash e nao vira duas extracoes. Nome do arquivo nao serve — a mesma
/// pessoa reenvia o anexo com outro nome o tempo todo, e dois arquivos diferentes podem
/// chegar com o mesmo nome.
/// <para>
/// SHA-256 e nao MD5 ou SHA-1: aqui a colisao nao seria so um azar estatistico, seria um
/// caminho para um arquivo se passar por outro que ja foi aprovado na revisao manual.
/// </para>
/// </remarks>
public readonly record struct HashDoArquivo
{
    public const int TamanhoEmCaracteres = 64;

    private HashDoArquivo(string texto) => Texto = texto;

    public string Texto { get; }

    public static HashDoArquivo De(ReadOnlySpan<byte> conteudo) =>
        new(Convert.ToHexStringLower(SHA256.HashData(conteudo)));

    public static HashDoArquivo DoTexto(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Length != TamanhoEmCaracteres)
        {
            throw new ArquivoRecusadoException($"Hash invalido: esperado {TamanhoEmCaracteres} caracteres.");
        }

        foreach (var caractere in texto)
        {
            if (!char.IsAsciiHexDigitLower(caractere))
            {
                throw new ArquivoRecusadoException("Hash invalido: so hexadecimal minusculo.");
            }
        }

        return new HashDoArquivo(texto);
    }

    public override string ToString() => Texto;

    /// <summary>
    /// A chave que este documento apresenta ao movimentar a conta.
    /// </summary>
    /// <remarks>
    /// Derivada do hash, e nao sorteada — mesmo raciocinio do desembolso no Core de
    /// Credito. Se o pagamento for reenviado, a conta reconhece o lancamento que ja existe
    /// em vez de debitar o boleto duas vezes.
    /// </remarks>
    public string ChaveDePagamento() =>
        string.Create(CultureInfo.InvariantCulture, $"documento:{Texto}");
}
