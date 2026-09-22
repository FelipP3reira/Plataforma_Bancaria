using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Banco.Aplicacao.Documentos;
using Banco.Aplicacao.Portas;
using Banco.Dominio.Documentos;
using Microsoft.Extensions.DependencyInjection;

namespace Banco.Testes.Integracao;

internal sealed record CampoHttp(
    string Nome,
    string ValorLido,
    decimal Confianca,
    string Origem,
    string? Observacao);

internal sealed record DetalheHttp(
    Guid Id,
    string Estado,
    int Tentativas,
    string? UltimoErro,
    decimal? Confianca,
    decimal? ConfiancaDoTexto,
    DateTimeOffset? ExtraidoEm,
    string? ConteudoExtraido,
    IReadOnlyList<CampoHttp> Campos)
{
    public CampoHttp? Campo(string nome) =>
        Campos.FirstOrDefault(campo => campo.Nome == nome);
}

/// <summary>
/// A fila de extracao contra o banco de verdade: a trava que evita dois workers no mesmo
/// documento, a reserva que vence e a desistencia depois de N tentativas.
/// </summary>
/// <remarks>
/// O pipeline e chamado direto, e nao pelo laco do worker. O que precisa ser provado aqui e
/// o comportamento de uma rodada contra o SQL Server; subir o processo do worker so
/// acrescentaria espera e um <c>Task.Delay</c> no meio do teste.
/// </remarks>
[Collection(ColecaoDaApi.Nome)]
public class FilaDeExtracaoTestes : IAsyncLifetime
{
    private readonly FabricaDaApi fabrica;

    public FilaDeExtracaoTestes(FabricaDaApi fabrica) => this.fabrica = fabrica;

    /// <summary>
    /// Deixa a fila vazia antes de cada teste.
    /// </summary>
    /// <remarks>
    /// A colecao compartilha um banco so, e outros testes deixam documento para tras — uns na
    /// fila, outros reservados. Teste de fila que nao comeca de fila conhecida mede o resto
    /// do vizinho. O relogio adiantado e o que torna as reservas alheias recolhiveis; ele
    /// volta ao lugar antes de o teste comecar.
    /// </remarks>
    public async Task InitializeAsync()
    {
        fabrica.Extrator.Reiniciar();

        fabrica.Relogio.Avancar(TimeSpan.FromDays(1));

        while (await UmaVarredura() > 0)
        {
            // Recolhe as reservas presas.
        }

        fabrica.Relogio.Reiniciar();

        while (await UmaRodada())
        {
            // Esvazia o que voltou para a fila.
        }

        fabrica.Extrator.Reiniciar();
    }

    public Task DisposeAsync()
    {
        fabrica.Extrator.Reiniciar();
        fabrica.Relogio.Reiniciar();

        return Task.CompletedTask;
    }

    /// <summary>Boleto de R$ 189,90 com vencimento em 10/10/2026.</summary>
    private const string LinhaDoBoleto = "00191234546789012345767890123457915950000018990";

    private static byte[] PdfDe(string miolo) =>
        [.. "%PDF-1.7\n"u8, .. System.Text.Encoding.ASCII.GetBytes(miolo)];

    private static async Task<Guid> Enviar(HttpClient cliente, Guid contaId, string miolo)
    {
        var corpo = new MultipartFormDataContent { { new StringContent(contaId.ToString()), "contaId" } };

        var arquivo = new ByteArrayContent(PdfDe(miolo));
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        corpo.Add(arquivo, "arquivo", "boleto.pdf");

        var pedido = new HttpRequestMessage(HttpMethod.Post, "/documentos") { Content = corpo };
        pedido.Headers.Add("X-Operador", "web:Ana Ribeiro");

        var resposta = await cliente.SendAsync(pedido);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<DocumentoHttp>(Pedidos.Json))!.Id;
    }

    private static Task<DetalheHttp?> Detalhe(HttpClient cliente, Guid id) =>
        cliente.GetFromJsonAsync<DetalheHttp>($"/documentos/{id}", Pedidos.Json);

    /// <summary>
    /// Uma rodada do pipeline, no seu proprio escopo — como o worker faz.
    /// </summary>
    private async Task<bool> UmaRodada()
    {
        await using var escopo = fabrica.Services.CreateAsyncScope();

        return await escopo.ServiceProvider
            .GetRequiredService<ExtrairProximoDocumento>()
            .Executar(CancellationToken.None);
    }

    private async Task<int> UmaVarredura()
    {
        await using var escopo = fabrica.Services.CreateAsyncScope();

        return await escopo.ServiceProvider
            .GetRequiredService<ExtrairProximoDocumento>()
            .RecuperarPresos(CancellationToken.None);
    }

    [Fact]
    public async Task ODocumentoSaiDaFilaEViraExtraido()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        var id = await Enviar(cliente, conta, "Vencimento 10/10/2026 Valor 189,90");

        Assert.True(await UmaRodada());

        var detalhe = await Detalhe(cliente, id);

        Assert.Equal("Extraido", detalhe!.Estado);
        Assert.Equal(1, detalhe.Tentativas);
        Assert.NotNull(detalhe.ExtraidoEm);
        Assert.Contains("Valor 189,90", detalhe.ConteudoExtraido!, StringComparison.Ordinal);
        Assert.InRange(detalhe.Confianca!.Value, 0m, 1m);
    }

    [Fact]
    public async Task FilaVaziaNaoGastaChamadaDeExtracao()
    {
        var antes = fabrica.Extrator.Chamadas;

        Assert.False(await UmaRodada());
        Assert.Equal(antes, fabrica.Extrator.Chamadas);
    }

    /// <summary>
    /// O que <c>READPAST</c> compra: dois workers em paralelo pegam documentos diferentes.
    /// </summary>
    /// <remarks>
    /// As duas reservas acontecem com transacoes abertas ao mesmo tempo, que e a unica
    /// situacao em que a dica de tabela importa. Sem <c>READPAST</c> o segundo SELECT
    /// esperaria pela linha travada pelo primeiro em vez de pular para a seguinte; sem
    /// <c>UPDLOCK</c> os dois leriam o mesmo documento e a extracao sairia duas vezes.
    /// </remarks>
    [Fact]
    public async Task DoisWorkersAoMesmoTempoPegamDocumentosDiferentes()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        await Enviar(cliente, conta, "primeiro boleto");
        await Enviar(cliente, conta, "segundo boleto");

        await using var primeiro = fabrica.Services.CreateAsyncScope();
        await using var segundo = fabrica.Services.CreateAsyncScope();

        var (umId, umaTransacao) = await Reservar(primeiro);
        var (outroId, outraTransacao) = await Reservar(segundo);

        await umaTransacao.Confirmar(CancellationToken.None);
        await outraTransacao.Confirmar(CancellationToken.None);
        await umaTransacao.DisposeAsync();
        await outraTransacao.DisposeAsync();

        Assert.NotNull(umId);
        Assert.NotNull(outroId);
        Assert.NotEqual(umId, outroId);
    }

    /// <summary>
    /// Worker que morre no meio nao deixa o documento preso: passado o prazo, ele volta a
    /// ser elegivel.
    /// </summary>
    [Fact]
    public async Task ReservaVencidaVoltaParaAFila()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        var id = await Enviar(cliente, conta, "boleto de worker derrubado");

        // Reserva e abandona sem gravar resultado: e exatamente o que um processo morto faz.
        await using (var escopo = fabrica.Services.CreateAsyncScope())
        {
            var (reservado, transacao) = await Reservar(escopo);
            await transacao.Confirmar(CancellationToken.None);
            await transacao.DisposeAsync();

            Assert.Equal(id, reservado);
        }

        Assert.Equal("Extraindo", (await Detalhe(cliente, id))!.Estado);

        // Antes do prazo, ninguem mexe: varredura que recolhesse cedo tiraria o documento
        // da mao de um worker que ainda esta trabalhando.
        Assert.Equal(0, await UmaVarredura());

        fabrica.Relogio.Avancar(TimeSpan.FromMinutes(5));

        Assert.Equal(1, await UmaVarredura());

        var depois = await Detalhe(cliente, id);

        Assert.Equal("Recebido", depois!.Estado);
        Assert.Equal(1, depois.Tentativas);
        Assert.Contains("Reserva vencida", depois.UltimoErro!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A dead-letter: falhou o numero maximo de vezes, para de tentar.
    /// </summary>
    [Fact]
    public async Task DocumentoQueFalhaTodasAsTentativasDesiste()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        var id = await Enviar(cliente, conta, "boleto que nao le");

        fabrica.Extrator.MensagemDeFalha = "arquivo corrompido";

        for (var tentativa = 1; tentativa <= Documento.MaximoDeTentativas; tentativa++)
        {
            Assert.True(await UmaRodada());
        }

        var detalhe = await Detalhe(cliente, id);

        Assert.Equal("Falhou", detalhe!.Estado);
        Assert.Equal(Documento.MaximoDeTentativas, detalhe.Tentativas);
        Assert.Null(detalhe.ConteudoExtraido);

        // Nao volta sozinho: a rodada seguinte nao encontra nada na fila.
        Assert.False(await UmaRodada());
    }

    /// <summary>
    /// O que vai para a coluna de erro e o tipo da excecao, nao a mensagem dela.
    /// </summary>
    /// <remarks>
    /// Mensagem de erro de leitura cita o trecho que nao entendeu, e esse trecho e conteudo
    /// de boleto: valor, CNPJ, nome do sacado. A coluna de erro e o campo que mais aparece
    /// em log e em tela de suporte, e nao pode ser por onde o dado sensivel escapa.
    /// </remarks>
    [Fact]
    public async Task OErroGravadoNaoCarregaConteudoDoDocumento()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        var id = await Enviar(cliente, conta, "boleto sigiloso");

        fabrica.Extrator.MensagemDeFalha = "nao entendi o trecho 'CNPJ 12.345.678/0001-95 valor 1.402,77'";

        Assert.True(await UmaRodada());

        var detalhe = await Detalhe(cliente, id);

        Assert.Equal("InvalidDataException", detalhe!.UltimoErro);
        Assert.DoesNotContain("12.345.678", detalhe.UltimoErro, StringComparison.Ordinal);
        Assert.DoesNotContain("1.402,77", detalhe.UltimoErro, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DocumentoQueDesistiuVoltaPelaRotaDeReprocessamento()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        var id = await Enviar(cliente, conta, "boleto para reprocessar");

        fabrica.Extrator.MensagemDeFalha = "provedor fora do ar";

        for (var tentativa = 1; tentativa <= Documento.MaximoDeTentativas; tentativa++)
        {
            await UmaRodada();
        }

        fabrica.Extrator.MensagemDeFalha = null;

        var resposta = await cliente.PostAsync($"/documentos/{id}/reprocessamento", content: null);

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);

        var reenfileirado = await Detalhe(cliente, id);

        Assert.Equal("Recebido", reenfileirado!.Estado);

        // O contador zera: sem isso o documento gastaria a unica tentativa que sobrou.
        Assert.Equal(0, reenfileirado.Tentativas);

        Assert.True(await UmaRodada());
        Assert.Equal("Extraido", (await Detalhe(cliente, id))!.Estado);
    }

    /// <summary>
    /// Documento que ainda esta na fila nao se reprocessa: nao ha o que recuperar, e aceitar
    /// seria zerar o contador de tentativas de um documento que esta tentando.
    /// </summary>
    [Fact]
    public async Task ReprocessarDocumentoQueNaoDesistiuEhRecusado()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        var id = await Enviar(cliente, conta, "boleto recem chegado");

        var resposta = await cliente.PostAsync($"/documentos/{id}/reprocessamento", content: null);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("Mudanca de estado invalida", await resposta.TituloDoProblema());
    }

    [Fact]
    public async Task ReprocessarDocumentoInexistenteDevolveNaoEncontrado() =>
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await fabrica.CreateClient()
                .PostAsync($"/documentos/{Guid.NewGuid()}/reprocessamento", content: null)).StatusCode);

    /// <summary>
    /// O caminho inteiro: arquivo com boleto entra e sai como campos pagaveis.
    /// </summary>
    [Fact]
    public async Task OBoletoNoArquivoViraCamposComConfiancaCheia()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        var id = await Enviar(cliente, conta, $"Energia SA\n{LinhaDoBoleto}\nPagavel em qualquer banco");

        Assert.True(await UmaRodada());

        var detalhe = await Detalhe(cliente, id);

        Assert.Equal("Extraido", detalhe!.Estado);
        Assert.Equal(1.00m, detalhe.Confianca);

        Assert.Equal(LinhaDoBoleto, detalhe.Campo("LinhaDigitavel")!.ValorLido);
        Assert.Equal("189.90", detalhe.Campo("Valor")!.ValorLido);
        Assert.Equal("2026-10-10", detalhe.Campo("Vencimento")!.ValorLido);
        Assert.All(detalhe.Campos, campo => Assert.Equal("Estrutura", campo.Origem));
    }

    /// <summary>
    /// As duas confiancas respondem perguntas diferentes, e e isso que a separacao compra: aqui o
    /// arquivo foi lido bem — confianca do texto alta — e o que foi lido nao e boleto nenhum.
    /// Uma so faria o cartaz legivel e o PDF ilegivel chegarem na fila com a mesma cara.
    /// </summary>
    [Fact]
    public async Task ArquivoLegivelQueNaoEhBoletoTemConfiancaZeroEDeTextoAlta()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        var id = await Enviar(cliente, conta, "Recibo de pagamento. Valor: 80,00. Vencimento 10/10/2026.");

        Assert.True(await UmaRodada());

        var detalhe = await Detalhe(cliente, id);

        Assert.Equal(0m, detalhe!.Confianca);
        Assert.True(
            detalhe.ConfiancaDoTexto > 0.5m,
            $"o texto foi lido bem; confianca do texto foi {detalhe.ConfiancaDoTexto}");

        Assert.Null(detalhe.Campo("LinhaDigitavel"));

        // O que deu para achar fica registrado, para quem for revisar na mao.
        Assert.Equal("80.00", detalhe.Campo("Valor")!.ValorLido);
        Assert.Equal("Texto", detalhe.Campo("Valor")!.Origem);
    }

    /// <summary>
    /// Boleto cujo valor impresso nao bate com o do codigo de barras: a assinatura do boleto
    /// adulterado. O valor gravado continua sendo o do codigo, que e o que o banco cobraria, e a
    /// confianca cai a ponto de nao passar por limiar nenhum.
    /// </summary>
    [Fact]
    public async Task ValorImpressoDivergenteDerrubaAConfiancaDoDocumento()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(0m);
        var id = await Enviar(cliente, conta, $"Valor do documento: 1.402,77\n{LinhaDoBoleto}");

        Assert.True(await UmaRodada());

        var detalhe = await Detalhe(cliente, id);
        var valor = detalhe!.Campo("Valor")!;

        Assert.Equal("189.90", valor.ValorLido);
        Assert.Equal(0.20m, detalhe.Confianca);
        Assert.NotNull(valor.Observacao);

        // A observacao explica a confianca sem repetir o conteudo do documento.
        Assert.DoesNotContain("1.402,77", valor.Observacao, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reserva uma linha da fila e devolve o id junto com a transacao ainda aberta, para o
    /// teste decidir quando solta-la.
    /// </summary>
    private static async Task<(Guid? Id, ITransacao Transacao)> Reservar(AsyncServiceScope escopo)
    {
        var unidade = escopo.ServiceProvider.GetRequiredService<IUnidadeDeTrabalho>();
        var documentos = escopo.ServiceProvider.GetRequiredService<IRepositorioDeDocumentos>();
        var relogio = escopo.ServiceProvider.GetRequiredService<TimeProvider>();

        var transacao = await unidade.Abrir(CancellationToken.None);
        var documento = await documentos.ProximoDaFila(CancellationToken.None);

        if (documento is null)
        {
            return (null, transacao);
        }

        documento.Reservar(relogio.GetUtcNow(), TimeSpan.FromMinutes(2));
        await unidade.Salvar(CancellationToken.None);

        return (documento.Id, transacao);
    }
}
