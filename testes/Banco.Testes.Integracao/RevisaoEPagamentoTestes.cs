using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Banco.Aplicacao.Documentos;
using Microsoft.Extensions.DependencyInjection;

namespace Banco.Testes.Integracao;

internal sealed record NaFilaHttp(
    Guid Id,
    Guid ContaId,
    decimal Confianca,
    IReadOnlyList<string> MotivosDosCampos);

internal sealed record PagamentoHttp(
    Guid DocumentoId,
    Guid ContaId,
    Guid LancamentoId,
    decimal Valor,
    string LinhaDigitavel,
    string Estado,
    bool Novo);

/// <summary>
/// A fila de revisao manual e o boleto virando debito na conta.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public class RevisaoEPagamentoTestes : IAsyncLifetime
{
    /// <summary>Boleto de R$ 189,90 com vencimento em 10/10/2026.</summary>
    private const string LinhaDoBoleto = "00191234546789012345767890123457915950000018990";

    private readonly FabricaDaApi fabrica;

    public RevisaoEPagamentoTestes(FabricaDaApi fabrica) => this.fabrica = fabrica;

    /// <summary>
    /// Esvazia a fila de extracao e a de revisao antes de cada teste: a colecao compartilha um
    /// banco so, e teste que nao comeca de estado conhecido mede o resto do vizinho.
    /// </summary>
    public async Task InitializeAsync()
    {
        fabrica.Extrator.Reiniciar();

        while (await UmaRodada())
        {
            // Esvazia a fila de extracao.
        }

        var cliente = fabrica.CreateClient();

        foreach (var pendente in await Fila(cliente))
        {
            await Conferir(cliente, pendente.Id);
        }
    }

    public Task DisposeAsync()
    {
        fabrica.Extrator.Reiniciar();
        fabrica.Relogio.Reiniciar();

        return Task.CompletedTask;
    }

    private async Task<bool> UmaRodada()
    {
        await using var escopo = fabrica.Services.CreateAsyncScope();

        return await escopo.ServiceProvider
            .GetRequiredService<ExtrairProximoDocumento>()
            .Executar(CancellationToken.None);
    }

    private static byte[] PdfDe(string miolo) =>
        [.. "%PDF-1.7\n"u8, .. System.Text.Encoding.ASCII.GetBytes(miolo)];

    /// <summary>Envia o arquivo e roda o pipeline: devolve o documento ja extraido.</summary>
    private async Task<Guid> Processar(HttpClient cliente, Guid contaId, string miolo)
    {
        var corpo = new MultipartFormDataContent { { new StringContent(contaId.ToString()), "contaId" } };

        var arquivo = new ByteArrayContent(PdfDe(miolo));
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        corpo.Add(arquivo, "arquivo", "boleto.pdf");

        var pedido = new HttpRequestMessage(HttpMethod.Post, "/documentos") { Content = corpo };
        pedido.Headers.Add("X-Operador", "web:Ana Ribeiro");

        var resposta = await cliente.SendAsync(pedido);
        resposta.EnsureSuccessStatusCode();

        var id = (await resposta.Content.ReadFromJsonAsync<DocumentoHttp>(Pedidos.Json))!.Id;

        Assert.True(await UmaRodada());

        return id;
    }

    private static Task<DetalheHttp?> Detalhe(HttpClient cliente, Guid id) =>
        cliente.GetFromJsonAsync<DetalheHttp>($"/documentos/{id}", Pedidos.Json);

    private static async Task<IReadOnlyList<NaFilaHttp>> Fila(HttpClient cliente) =>
        await cliente.GetFromJsonAsync<List<NaFilaHttp>>("/documentos/revisao", Pedidos.Json) ?? [];

    private static Task<HttpResponseMessage> Conferir(
        HttpClient cliente,
        Guid id,
        Dictionary<string, string>? correcoes = null,
        string? operador = "web:Bruno Salgado")
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/documentos/{id}/revisao")
        {
            Content = JsonContent.Create(new { correcoes = correcoes ?? [] }),
        };

        if (operador is not null)
        {
            pedido.Headers.Add("X-Operador", operador);
        }

        return cliente.SendAsync(pedido);
    }

    private static Task<HttpResponseMessage> Pagar(
        HttpClient cliente,
        Guid id,
        string? operador = "web:Bruno Salgado")
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/documentos/{id}/pagamento");

        if (operador is not null)
        {
            pedido.Headers.Add("X-Operador", operador);
        }

        return cliente.SendAsync(pedido);
    }

    /// <summary>Boleto limpo: a linha digitavel fecha e nada divergo — passa sozinho.</summary>
    [Fact]
    public async Task BoletoLegivelPassaDiretoSemRevisao()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(500m);
        var id = await Processar(cliente, conta, LinhaDoBoleto);

        Assert.Equal("Extraido", (await Detalhe(cliente, id))!.Estado);
        Assert.Empty(await Fila(cliente));
    }

    /// <summary>
    /// Confianca abaixo do limiar para na fila, e o dinheiro fica parado ate alguem confirmar. E o
    /// ponto inteiro da fila de revisao.
    /// </summary>
    [Fact]
    public async Task ValorDivergenteParaNaFilaENaoPodeSerPago()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(2000m);
        var id = await Processar(cliente, conta, $"Valor do documento: 1.402,77\n{LinhaDoBoleto}");

        Assert.Equal("RequerRevisao", (await Detalhe(cliente, id))!.Estado);

        var fila = await Fila(cliente);
        var pendente = Assert.Single(fila);

        Assert.Equal(id, pendente.Id);
        Assert.Equal(0.20m, pendente.Confianca);

        // O motivo do campo pior vem primeiro, para a tela abrir no lugar certo.
        Assert.StartsWith("Valor: 0,20", pendente.MotivosDosCampos[0], StringComparison.Ordinal);

        var recusa = await Pagar(cliente, id);

        Assert.Equal(HttpStatusCode.Conflict, recusa.StatusCode);
        Assert.Equal(2000m, (await cliente.Detalhe(conta))!.Saldo);
    }

    /// <summary>
    /// A fila e ordenada por gravidade, e nao por ordem de chegada: o boleto adulterado precisa ser
    /// visto antes do que so tem o CNPJ ambiguo.
    /// </summary>
    [Fact]
    public async Task AFilaVemDoMenosConfiavelParaOMais()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);

        // Sem linha digitavel: confianca zero, o pior caso.
        var semBoleto = await Processar(cliente, conta, "Recibo. Valor: 80,00.");

        // Com linha digitavel e valor divergente: 0,20.
        var divergente = await Processar(cliente, conta, $"Valor: 1.402,77\n{LinhaDoBoleto}");

        var fila = await Fila(cliente);

        Assert.Equal([semBoleto, divergente], fila.Select(documento => documento.Id));
    }

    [Fact]
    public async Task ConferirSemCorrecaoFechaARevisao()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(500m);
        var id = await Processar(cliente, conta, $"Valor: 1.402,77\n{LinhaDoBoleto}");

        var resposta = await Conferir(cliente, id);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal("Revisado", (await Detalhe(cliente, id))!.Estado);
        Assert.Empty(await Fila(cliente));
    }

    /// <summary>
    /// A correcao fica ao lado do valor lido, com quem e quando — e o lido continua la.
    /// </summary>
    [Fact]
    public async Task ACorrecaoEGravadaAoLadoDoQueAMaquinaLeu()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(2000m);
        var id = await Processar(cliente, conta, $"Valor: 1.402,77\n{LinhaDoBoleto}");

        await Conferir(cliente, id, new Dictionary<string, string> { ["Valor"] = "1.402,77" });

        var valor = (await Detalhe(cliente, id))!.Campo("Valor")!;

        Assert.Equal("189.90", valor.ValorLido);
        Assert.Equal("1402.77", valor.ValorFinal);
        Assert.Equal("web:Bruno Salgado", valor.CorrigidoPor);
        Assert.NotNull(valor.CorrigidoEm);
    }

    /// <summary>
    /// A revisao e a unica porta por onde um valor entra sem passar pelo verificador da leitura. Sem
    /// conferir aqui, corrigir a linha digitavel para qualquer coisa fecharia o documento e o
    /// pagamento cobraria o que foi digitado.
    /// </summary>
    [Fact]
    public async Task LinhaDigitavelCorrigidaParaAlgoInvalidoEhRecusada()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        var id = await Processar(cliente, conta, $"Valor: 1.402,77\n{LinhaDoBoleto}");

        var resposta = await Conferir(
            cliente,
            id,
            new Dictionary<string, string> { ["LinhaDigitavel"] = "11111111111111111111111111111111111111111111111" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);

        // E o documento continua esperando revisao, e nao meio corrigido.
        Assert.Equal("RequerRevisao", (await Detalhe(cliente, id))!.Estado);
    }

    /// <summary>
    /// Nome de campo errado e recusa e nao silencio: correcao ignorada por erro de digitacao faria
    /// quem revisou acreditar que consertou o valor.
    /// </summary>
    [Fact]
    public async Task CampoDesconhecidoNaCorrecaoEhRecusado()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        var id = await Processar(cliente, conta, $"Valor: 1.402,77\n{LinhaDoBoleto}");

        var resposta = await Conferir(cliente, id, new Dictionary<string, string> { ["valorr"] = "10,00" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task RevisaoSemOperadorEhRecusada()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        var id = await Processar(cliente, conta, $"Valor: 1.402,77\n{LinhaDoBoleto}");

        Assert.Equal(HttpStatusCode.BadRequest, (await Conferir(cliente, id, operador: null)).StatusCode);
    }

    /// <summary>O caminho completo: arquivo entra, boleto sai, conta paga.</summary>
    [Fact]
    public async Task OBoletoViraDebitoNaConta()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(500m);
        var id = await Processar(cliente, conta, LinhaDoBoleto);

        var resposta = await Pagar(cliente, id);

        Assert.True(
            resposta.StatusCode == HttpStatusCode.Created,
            $"esperava 201 e veio {resposta.StatusCode}: {await resposta.Content.ReadAsStringAsync()}");

        var pagamento = await resposta.Content.ReadFromJsonAsync<PagamentoHttp>(Pedidos.Json);

        Assert.True(pagamento!.Novo);
        Assert.Equal(189.90m, pagamento.Valor);
        Assert.Equal("Pago", pagamento.Estado);

        Assert.Equal(310.10m, (await cliente.Detalhe(conta))!.Saldo);

        // O documento aponta para o dinheiro que saiu por causa dele.
        var detalhe = await Detalhe(cliente, id);

        Assert.Equal(pagamento.LancamentoId, detalhe!.LancamentoDoPagamentoId);
        Assert.NotNull(detalhe.PagoEm);

        // E o debito aparece no extrato com a linha digitavel na descricao.
        var extrato = await cliente.Extrato(conta);

        Assert.Contains(
            extrato.Linhas,
            linha => linha.Descricao.Contains(LinhaDoBoleto, StringComparison.Ordinal));
    }

    /// <summary>
    /// A chave de idempotencia e derivada do hash do arquivo: repetir o pedido nao cobra de novo.
    /// </summary>
    [Fact]
    public async Task PagarDuasVezesNaoCobraDuasVezes()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(500m);
        var id = await Processar(cliente, conta, LinhaDoBoleto);

        var primeira = await (await Pagar(cliente, id)).Content.ReadFromJsonAsync<PagamentoHttp>(Pedidos.Json);
        var repeticao = await Pagar(cliente, id);
        var segunda = await repeticao.Content.ReadFromJsonAsync<PagamentoHttp>(Pedidos.Json);

        Assert.Equal(HttpStatusCode.OK, repeticao.StatusCode);
        Assert.False(segunda!.Novo);
        Assert.Equal(primeira!.LancamentoId, segunda.LancamentoId);

        Assert.Equal(310.10m, (await cliente.Detalhe(conta))!.Saldo);
    }

    /// <summary>
    /// O pagamento le o valor <em>final</em> do campo. Ler o que a maquina extraiu cobraria o
    /// numero errado justamente nos documentos que passaram pela revisao.
    /// </summary>
    [Fact]
    public async Task OPagamentoCobraOValorCorrigidoENaoOLido()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(2000m);
        var id = await Processar(cliente, conta, $"Valor: 1.402,77\n{LinhaDoBoleto}");

        await Conferir(cliente, id, new Dictionary<string, string> { ["Valor"] = "1.402,77" });

        var pagamento = await (await Pagar(cliente, id)).Content.ReadFromJsonAsync<PagamentoHttp>(Pedidos.Json);

        Assert.Equal(1402.77m, pagamento!.Valor);
        Assert.Equal(597.23m, (await cliente.Detalhe(conta))!.Saldo);
    }

    /// <summary>
    /// Saldo insuficiente recusa o pagamento e o documento <b>nao</b> vira pago. Na ordem inversa,
    /// um debito recusado deixaria o documento dizendo que foi pago sem dinheiro nenhum ter saido.
    /// </summary>
    [Fact]
    public async Task SemSaldoOPagamentoEhRecusadoEODocumentoNaoFechaComoPago()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(10m);
        var id = await Processar(cliente, conta, LinhaDoBoleto);

        var resposta = await Pagar(cliente, id);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("Saldo insuficiente", await resposta.TituloDoProblema());

        Assert.Equal("Extraido", (await Detalhe(cliente, id))!.Estado);
        Assert.Equal(10m, (await cliente.Detalhe(conta))!.Saldo);
    }

    /// <summary>
    /// Conta bloqueada recebe documento e nao paga. Receber um boleto nao move dinheiro; pagar move
    /// — e e o pagamento que o bloqueio existe para barrar.
    /// </summary>
    [Fact]
    public async Task ContaBloqueadaNaoPagaBoleto()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(500m);
        var id = await Processar(cliente, conta, LinhaDoBoleto);

        await cliente.Bloquear(conta, "suspeita de fraude");

        var resposta = await Pagar(cliente, id);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("Conta nao movimenta", await resposta.TituloDoProblema());
        Assert.Equal("Extraido", (await Detalhe(cliente, id))!.Estado);
    }

    /// <summary>
    /// Boleto sem valor no codigo e sem valor impresso nao se paga sozinho: valor a combinar e
    /// legitimo, cobrar um numero que ninguem definiu nao e.
    /// </summary>
    [Fact]
    public async Task BoletoSemValorNaoPodeSerPagoAntesDeAlguemDizerQuanto()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(500m);

        // Mesmo boleto, com o valor zerado no codigo de barras.
        const string semValor = "23791234546789012345767890123457615950000000000";

        var id = await Processar(cliente, conta, semValor);

        await Conferir(cliente, id);

        var recusa = await Pagar(cliente, id);

        Assert.Equal(HttpStatusCode.BadRequest, recusa.StatusCode);

        // Depois de alguem dizer quanto, paga.
        // (O documento ja esta Revisado; a correcao entra por um segundo documento.)
        Assert.Equal(500m, (await cliente.Detalhe(conta))!.Saldo);
    }

    [Fact]
    public async Task PagamentoSemOperadorEhRecusado()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(500m);
        var id = await Processar(cliente, conta, LinhaDoBoleto);

        Assert.Equal(HttpStatusCode.BadRequest, (await Pagar(cliente, id, operador: null)).StatusCode);
    }

    [Fact]
    public async Task PagarDocumentoInexistenteDevolveNaoEncontrado() =>
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Pagar(fabrica.CreateClient(), Guid.NewGuid())).StatusCode);
}
