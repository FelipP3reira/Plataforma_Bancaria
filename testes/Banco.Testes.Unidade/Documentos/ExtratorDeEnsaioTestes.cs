using System.Text;
using Banco.Dominio.Documentos;
using Banco.Infraestrutura.Documentos;

namespace Banco.Testes.Unidade.Documentos;

/// <summary>
/// O extrator local. O que precisa valer dele e ser deterministico e nao inventar confianca.
/// </summary>
public class ExtratorDeEnsaioTestes
{
    private static async Task<(string Conteudo, decimal Confianca)> Extrair(params byte[] bytes)
    {
        using var conteudo = new MemoryStream(bytes);
        var texto = await new ExtratorDeEnsaio()
            .Extrair(conteudo, TipoDeArquivo.Pdf, CancellationToken.None);

        return (texto.Conteudo, texto.Confianca);
    }

    private static byte[] PdfCom(string miolo) =>
        [.. "%PDF-1.7\n"u8, .. Encoding.ASCII.GetBytes(miolo)];

    [Fact]
    public async Task LeOTextoLegivelDoArquivo()
    {
        var (conteudo, _) = await Extrair(PdfCom("Vencimento 10/10/2026 Valor 189,90"));

        Assert.Contains("Vencimento 10/10/2026 Valor 189,90", conteudo, StringComparison.Ordinal);
    }

    /// <summary>
    /// A propriedade que faz um teste de fila poder existir: sem ela, "o documento foi
    /// extraido?" nao teria resposta estavel.
    /// </summary>
    [Fact]
    public async Task OMesmoArquivoDaSempreOMesmoResultado()
    {
        var primeira = await Extrair(PdfCom("boleto de luz 87,40"));
        var segunda = await Extrair(PdfCom("boleto de luz 87,40"));

        Assert.Equal(primeira, segunda);
    }

    /// <summary>
    /// Byte binario corta o trecho em vez de emendar os dois lados: sem isso, palavras que
    /// nao se tocam no documento sairiam coladas no texto.
    /// </summary>
    [Fact]
    public async Task ByteBinarioSeparaOsTrechos()
    {
        var (conteudo, _) = await Extrair([.. "Emissor"u8, 0x00, 0xFF, .. "Sacado"u8]);

        Assert.DoesNotContain("EmissorSacado", conteudo, StringComparison.Ordinal);
        Assert.Contains("Emissor", conteudo, StringComparison.Ordinal);
        Assert.Contains("Sacado", conteudo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Arquivo sem texto nenhum devolve confianca baixa em vez de erro: e um caso para
    /// revisao humana, nao uma falha do pipeline.
    /// </summary>
    [Fact]
    public async Task ArquivoTodoBinarioSaiComConfiancaBaixa()
    {
        var (conteudo, confianca) = await Extrair([.. new byte[512]]);

        Assert.Empty(conteudo);
        Assert.Equal(0m, confianca);
    }

    [Fact]
    public async Task AConfiancaFicaNaFaixaDeZeroAUm()
    {
        var (_, confianca) = await Extrair(PdfCom(new string('a', 4096)));

        Assert.InRange(confianca, 0m, 1m);
    }

    /// <summary>
    /// Arquivo com mais binario que texto confia menos do que arquivo todo legivel. E o que
    /// faz PDF comprimido cair na revisao em vez de passar como extraido.
    /// </summary>
    [Fact]
    public async Task MaisBinarioSignificaMenosConfianca()
    {
        var legivel = await Extrair(PdfCom(new string('a', 500)));
        var misturado = await Extrair([.. PdfCom(new string('a', 500)), .. new byte[2000]]);

        Assert.True(
            misturado.Confianca < legivel.Confianca,
            $"esperava menos confianca no arquivo com binario: {misturado.Confianca} vs {legivel.Confianca}");
    }
}
