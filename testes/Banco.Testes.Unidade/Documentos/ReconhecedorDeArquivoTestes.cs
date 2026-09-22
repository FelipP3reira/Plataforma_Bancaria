using System.Text;
using Banco.Dominio.Documentos;
using Banco.Dominio.Erros;

namespace Banco.Testes.Unidade.Documentos;

/// <summary>
/// A porta de entrada do upload, que é o vetor de ataque mais comum da feature.
/// </summary>
public class ReconhecedorDeArquivoTestes
{
    private static byte[] Pdf() => [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    [Fact]
    public void ReconhecePdf() => Assert.Equal(TipoDeArquivo.Pdf, ReconhecedorDeArquivo.Reconhecer(Pdf()));

    [Fact]
    public void ReconhecePng() => Assert.Equal(TipoDeArquivo.Png, ReconhecedorDeArquivo.Reconhecer(Png()));

    /// <summary>
    /// O quarto byte do JPEG muda com a variante (JFIF, Exif, e outras). Conferi-lo
    /// recusaria foto legítima tirada de celular.
    /// </summary>
    [Theory]
    [InlineData(0xE0)]
    [InlineData(0xE1)]
    [InlineData(0xDB)]
    public void ReconheceAsVariantesDeJpeg(byte quartoByte)
    {
        byte[] conteudo = [0xFF, 0xD8, 0xFF, quartoByte, 0x00, 0x10, 0x4A, 0x46];

        Assert.Equal(TipoDeArquivo.Jpeg, ReconhecedorDeArquivo.Reconhecer(conteudo));
    }

    /// <summary>
    /// O ataque clássico do upload: executável com extensão de documento. A extensão não
    /// participa da decisão, então não há o que enganar.
    /// </summary>
    [Fact]
    public void ExecutavelEhRecusadoMesmoComCaraDePdf()
    {
        // "MZ" — todo executável de Windows começa assim.
        byte[] executavel = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];

        Assert.Throws<ArquivoRecusadoException>(() => ReconhecedorDeArquivo.Reconhecer(executavel));
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\">")]
    [InlineData("<!DOCTYPE html><html><script>alert(1)</script>")]
    [InlineData("PK\u0003\u0004zip-disfarcado-de-docx")]
    public void OutrosFormatosSaoRecusados(string conteudo) =>
        Assert.Throws<ArquivoRecusadoException>(
            () => ReconhecedorDeArquivo.Reconhecer(Encoding.ASCII.GetBytes(conteudo)));

    /// <summary>
    /// Assinatura certa no lugar errado não vale: o formato se declara no começo do
    /// arquivo, e aceitar em qualquer posição deixaria passar qualquer coisa com um
    /// "%PDF-" enterrado no meio.
    /// </summary>
    [Fact]
    public void AssinaturaNoMeioDoArquivoNaoVale()
    {
        byte[] disfarcado = [0x00, 0x00, 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31];

        Assert.Throws<ArquivoRecusadoException>(() => ReconhecedorDeArquivo.Reconhecer(disfarcado));
    }

    [Fact]
    public void ArquivoVazioEhRecusado() =>
        Assert.Throws<ArquivoRecusadoException>(() => ReconhecedorDeArquivo.Reconhecer([]));

    // A extensão gravada vem do tipo reconhecido, e não do nome que chegou.
    [Theory]
    [InlineData(TipoDeArquivo.Pdf, ".pdf")]
    [InlineData(TipoDeArquivo.Png, ".png")]
    [InlineData(TipoDeArquivo.Jpeg, ".jpg")]
    public void AExtensaoSaiDoTipoReconhecido(TipoDeArquivo tipo, string esperada) =>
        Assert.Equal(esperada, ReconhecedorDeArquivo.ExtensaoDe(tipo));

    [Fact]
    public void OitoBytesBastamParaDecidir() =>
        Assert.Equal(8, ReconhecedorDeArquivo.BytesNecessarios);
}
