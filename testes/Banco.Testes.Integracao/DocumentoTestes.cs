using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Banco.Testes.Integracao;

internal sealed record DocumentoHttp(
    Guid Id,
    Guid ContaId,
    string NomeOriginal,
    string Tipo,
    long TamanhoEmBytes,
    string Hash,
    string Estado,
    bool Novo);

/// <summary>
/// O upload de documento financeiro — a porta de entrada da extração, e o vetor de ataque
/// mais comum da feature.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public class DocumentoTestes
{
    private readonly FabricaDaApi fabrica;

    public DocumentoTestes(FabricaDaApi fabrica) => this.fabrica = fabrica;

    private static byte[] PdfDe(string miolo) =>
        [.. new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 }, .. System.Text.Encoding.ASCII.GetBytes(miolo)];

    private static Task<HttpResponseMessage> Enviar(
        HttpClient cliente,
        Guid contaId,
        byte[] conteudo,
        string nome = "boleto.pdf",
        string? operador = "web:Ana Ribeiro")
    {
        var corpo = new MultipartFormDataContent { { new StringContent(contaId.ToString()), "contaId" } };

        var arquivo = new ByteArrayContent(conteudo);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        corpo.Add(arquivo, "arquivo", nome);

        var pedido = new HttpRequestMessage(HttpMethod.Post, "/documentos") { Content = corpo };

        if (operador is not null)
        {
            pedido.Headers.Add("X-Operador", operador);
        }

        return cliente.SendAsync(pedido);
    }

    private static Task<DocumentoHttp?> Corpo(HttpResponseMessage resposta) =>
        resposta.Content.ReadFromJsonAsync<DocumentoHttp>(Pedidos.Json);

    [Fact]
    public async Task ODocumentoEntraNaFilaEVolta202()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);

        var resposta = await Enviar(cliente, conta, PdfDe("boleto de luz"));
        var documento = await Corpo(resposta);

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        Assert.True(documento!.Novo);
        Assert.Equal("Recebido", documento.Estado);
        Assert.Equal("Pdf", documento.Tipo);
        Assert.Equal(conta, documento.ContaId);
    }

    /// <summary>
    /// A idempotência do upload: a chave é o conteúdo do arquivo, não algo que o cliente
    /// precise lembrar de reenviar. Mesmo boleto duas vezes é uma extração só.
    /// </summary>
    [Fact]
    public async Task OMesmoArquivoEnviadoDuasVezesNaoViraDoisDocumentos()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        var conteudo = PdfDe("mesmo boleto");

        var primeira = await Corpo(await Enviar(cliente, conta, conteudo));
        var segunda = await Enviar(cliente, conta, conteudo, nome: "outro-nome.pdf");
        var repetida = await Corpo(segunda);

        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.False(repetida!.Novo);
        Assert.Equal(primeira!.Id, repetida.Id);

        // O nome mudou e o documento é o mesmo: o que identifica é o conteúdo.
        Assert.Equal("boleto.pdf", repetida.NomeOriginal);
    }

    /// <summary>
    /// A idempotência é por conta. Duas pessoas podem legitimamente receber o mesmo boleto
    /// — conta de luz de imóvel dividido — e cada uma paga a sua.
    /// </summary>
    [Fact]
    public async Task OMesmoArquivoEmContasDiferentesSaoDoisDocumentos()
    {
        var cliente = fabrica.CreateClient();
        var minha = await cliente.ContaCom(0m);
        var sua = await cliente.ContaCom(0m);
        var conteudo = PdfDe("boleto compartilhado");

        var meu = await Corpo(await Enviar(cliente, minha, conteudo));
        var seu = await Corpo(await Enviar(cliente, sua, conteudo));

        Assert.True(seu!.Novo);
        Assert.NotEqual(meu!.Id, seu.Id);
        Assert.Equal(meu.Hash, seu.Hash);
    }

    /// <summary>
    /// O ataque clássico do upload: executável com nome de PDF e Content-Type de PDF. As
    /// duas coisas são escolhidas por quem envia; só os bytes não são.
    /// </summary>
    [Fact]
    public async Task ExecutavelComNomeDePdfEhRecusado()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        byte[] executavel = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00];

        var resposta = await Enviar(cliente, conta, executavel, nome: "boleto.pdf");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, resposta.StatusCode);
        Assert.Equal("Arquivo recusado", await resposta.TituloDoProblema());
    }

    [Fact]
    public async Task ArquivoVazioEhRecusado()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);

        var resposta = await Enviar(cliente, conta, []);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task DocumentoSemOperadorEhRecusado()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);

        var resposta = await Enviar(cliente, conta, PdfDe("sem dono"), operador: null);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task DocumentoDeContaInexistenteEhRecusado()
    {
        var resposta = await Enviar(fabrica.CreateClient(), Guid.NewGuid(), PdfDe("orfao"));

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    /// <summary>
    /// Os bytes ficam fora de qualquer pasta servida pela aplicação, e o nome em disco é o
    /// hash — nada do que quem enviou escolheu entra no caminho.
    /// </summary>
    [Fact]
    public async Task OsBytesFicamEmDiscoComONomeDoHash()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);

        var documento = await Corpo(await Enviar(cliente, conta, PdfDe("em disco")));

        var esperado = Path.Combine(
            fabrica.PastaDeDocumentos,
            documento!.Hash[..2],
            $"{documento.Hash}.pdf");

        Assert.True(File.Exists(esperado), $"esperava o arquivo em {esperado}");
    }

    /// <summary>
    /// Caminho no nome do arquivo não escapa da pasta: o nome é sanitizado antes de ser
    /// gravado, e o caminho real vem do hash de qualquer forma.
    /// </summary>
    [Fact]
    public async Task NomeComCaminhoNaoEscapaDaPasta()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);

        var documento = await Corpo(
            await Enviar(cliente, conta, PdfDe("travessia"), nome: "../../../escapou.pdf"));

        Assert.Equal("escapou.pdf", documento!.NomeOriginal);
        Assert.False(
            File.Exists(Path.Combine(fabrica.PastaDeDocumentos, "..", "..", "..", "escapou.pdf")),
            "o arquivo nao pode ter saido da pasta de documentos");
    }

    [Fact]
    public async Task ODetalheDoDocumentoVoltaOQueFoiGravado()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        var enviado = await Corpo(await Enviar(cliente, conta, PdfDe("consulta")));

        var detalhe = await cliente.GetFromJsonAsync<DocumentoHttp>(
            $"/documentos/{enviado!.Id}",
            Pedidos.Json);

        Assert.Equal(enviado.Id, detalhe!.Id);
        Assert.Equal(enviado.Hash, detalhe.Hash);
        Assert.Equal("Recebido", detalhe.Estado);
    }

    [Fact]
    public async Task DocumentoInexistenteDevolveNaoEncontrado() =>
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await fabrica.CreateClient().GetAsync($"/documentos/{Guid.NewGuid()}")).StatusCode);

    /// <summary>
    /// Conta bloqueada continua recebendo documento: bloqueio impede movimentar, e receber
    /// um boleto não move dinheiro. O que vai ser barrado é o pagamento, lá na frente.
    /// </summary>
    [Fact]
    public async Task ContaBloqueadaAindaRecebeDocumento()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(100m);
        await cliente.Bloquear(conta, "suspeita");

        var resposta = await Enviar(cliente, conta, PdfDe("boleto de conta bloqueada"));

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
    }
}
