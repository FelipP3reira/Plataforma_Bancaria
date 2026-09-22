using System.Collections.Frozen;
using Banco.Dominio.Erros;

namespace Banco.Dominio.Documentos;

public enum TipoDeArquivo
{
    Pdf = 1,
    Png = 2,
    Jpeg = 3,
}

/// <summary>
/// Descobre o tipo do arquivo pelos primeiros bytes dele.
/// </summary>
/// <remarks>
/// A extensao e o <c>Content-Type</c> nao participam da decisao, e isso e o ponto. Os dois
/// sao escolhidos por quem envia: um executavel renomeado para <c>.pdf</c> chega com
/// extensao de PDF e com o cabecalho que o cliente quiser declarar. O unico dado que o
/// remetente nao controla de graca e o conteudo — e e nele que a conferencia mora.
/// <para>
/// Lista fechada, e nao lista de bloqueio: entra o que esta aqui, o resto e recusado. Lista
/// do que e proibido erra por omissao toda vez que aparece um formato novo.
/// </para>
/// </remarks>
public static class ReconhecedorDeArquivo
{
    /// <summary>
    /// Os bytes que abrem cada formato aceito.
    /// </summary>
    /// <remarks>
    /// JPEG tem assinatura de tres bytes porque o quarto varia com a variante (JFIF, Exif,
    /// e outras). Conferir o quarto byte recusaria foto legitima tirada de celular.
    /// </remarks>
    private static readonly FrozenDictionary<TipoDeArquivo, byte[]> Assinaturas =
        new Dictionary<TipoDeArquivo, byte[]>
        {
            [TipoDeArquivo.Pdf] = [0x25, 0x50, 0x44, 0x46, 0x2D],
            [TipoDeArquivo.Png] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
            [TipoDeArquivo.Jpeg] = [0xFF, 0xD8, 0xFF],
        }.ToFrozenDictionary();

    /// <summary>Quantos bytes bastam para decidir. O maior prefixo da tabela.</summary>
    public const int BytesNecessarios = 8;

    public static TipoDeArquivo Reconhecer(ReadOnlySpan<byte> inicio)
    {
        foreach (var (tipo, assinatura) in Assinaturas)
        {
            if (inicio.StartsWith(assinatura))
            {
                return tipo;
            }
        }

        throw new ArquivoRecusadoException(
            "O arquivo nao e PDF, PNG nem JPEG. O que vale e o conteudo, nao a extensao.");
    }

    public static string ExtensaoDe(TipoDeArquivo tipo) => tipo switch
    {
        TipoDeArquivo.Pdf => ".pdf",
        TipoDeArquivo.Png => ".png",
        TipoDeArquivo.Jpeg => ".jpg",
        _ => throw new ArquivoRecusadoException($"Tipo {tipo} sem extensao definida."),
    };

    public static string MediaTypeDe(TipoDeArquivo tipo) => tipo switch
    {
        TipoDeArquivo.Pdf => "application/pdf",
        TipoDeArquivo.Png => "image/png",
        TipoDeArquivo.Jpeg => "image/jpeg",
        _ => throw new ArquivoRecusadoException($"Tipo {tipo} sem media type definido."),
    };
}
