using Banco.Aplicacao.Documentos;
using Banco.Dominio.Documentos;
using Microsoft.AspNetCore.Mvc;

namespace Banco.Api.Documentos;

internal static class EndpointsDeDocumentos
{
    private const string CabecalhoDeOperador = "X-Operador";

    public static void MapearDocumentos(this IEndpointRouteBuilder rotas)
    {
        var documentos = rotas.MapGroup("/documentos").WithTags("Documentos");

        documentos.MapPost("/", Receber)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(Documento.TamanhoMaximoEmBytes));

        // Antes da rota com parametro: "/documentos/revisao" e "/documentos/{id}" competem, e a
        // literal precisa ganhar. Como o parametro e restrito a guid, "revisao" nem casaria — a
        // ordem esta aqui para que a rota continue funcionando se um dia a restricao sair.
        documentos.MapGet("/revisao", Revisar);

        documentos.MapGet("/{id:guid}", Detalhar);

        documentos.MapPost("/{id:guid}/reprocessamento", Reprocessar);
        documentos.MapPost("/{id:guid}/revisao", Conferir);
        documentos.MapPost("/{id:guid}/pagamento", Pagar);
    }

    /// <summary>
    /// Recebe o arquivo e devolve 202: o resultado da extracao vem depois.
    /// </summary>
    /// <remarks>
    /// Sem <c>Idempotency-Key</c>, ao contrario das movimentacoes. Aqui a chave nao vem de
    /// quem chama porque ela ja esta no proprio arquivo: o SHA-256 do conteudo identifica o
    /// documento melhor do que qualquer texto que o cliente inventasse, e nao depende de
    /// ele lembrar de reenviar a mesma chave.
    /// </remarks>
    private static async Task<IResult> Receber(
        [FromForm] Guid contaId,
        IFormFile arquivo,
        [FromHeader(Name = CabecalhoDeOperador)] string? operador,
        ReceberDocumento receber,
        CancellationToken cancelamento)
    {
        if (SemOperador(operador) is { } recusa)
        {
            return recusa;
        }

        if (arquivo is null || arquivo.Length == 0)
        {
            return Results.Problem(
                title: "Arquivo obrigatorio",
                detail: "Envie o documento no campo 'arquivo'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // O tamanho e conferido aqui, antes de ler: sem isto, recusar um arquivo grande
        // demais custaria le-lo inteiro para a memoria primeiro.
        if (arquivo.Length > Documento.TamanhoMaximoEmBytes)
        {
            return Results.Problem(
                title: "Arquivo grande demais",
                detail: $"O limite e {Documento.TamanhoMaximoEmBytes / (1024 * 1024)} MB.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        using var memoria = new MemoryStream(capacity: (int)arquivo.Length);
        await arquivo.CopyToAsync(memoria, cancelamento).ConfigureAwait(false);

        var recebido = await receber
            .Executar(
                new PedidoDeDocumento(contaId, arquivo.FileName, memoria.ToArray(), operador!),
                cancelamento)
            .ConfigureAwait(false);

        // 202 na primeira vez e 200 no reenvio: quem chama distingue "entrou na fila agora"
        // de "ja estava la" sem comparar corpo.
        return recebido.Novo
            ? Results.Accepted($"/documentos/{recebido.Id}", recebido)
            : Results.Ok(recebido);
    }

    /// <summary>
    /// Devolve a fila um documento que esgotou as tentativas.
    /// </summary>
    /// <remarks>
    /// 202 e nao 200: a resposta diz que o documento voltou para a fila, e nao que ele foi
    /// extraido. Quem extrai e o worker, depois.
    /// </remarks>
    private static async Task<IResult> Reprocessar(
        Guid id,
        ReenfileirarDocumento reenfileirar,
        CancellationToken cancelamento)
    {
        await reenfileirar.Executar(id, cancelamento).ConfigureAwait(false);

        return Results.Accepted($"/documentos/{id}");
    }

    /// <summary>A fila de documentos esperando olho humano, os piores primeiro.</summary>
    private static async Task<IResult> Revisar(
        int? limite,
        ListarFilaDeRevisao listar,
        CancellationToken cancelamento) =>
        Results.Ok(await listar.Executar(limite, cancelamento).ConfigureAwait(false));

    /// <summary>
    /// Registra a conferencia humana, com as correcoes que houver.
    /// </summary>
    /// <remarks>
    /// O corpo pode vir sem correcao nenhuma: "olhei e esta certo" e um resultado, e o mais comum.
    /// Exigir correcao para poder confirmar levaria quem revisa a inventar uma.
    /// </remarks>
    private static async Task<IResult> Conferir(
        Guid id,
        RevisaoHttp? corpo,
        [FromHeader(Name = CabecalhoDeOperador)] string? operador,
        RevisarDocumento revisar,
        CancellationToken cancelamento)
    {
        if (SemOperador(operador) is { } recusa)
        {
            return recusa;
        }

        await revisar
            .Executar(
                id,
                new PedidoDeRevisao(corpo?.Correcoes ?? new Dictionary<string, string>(), operador!),
                cancelamento)
            .ConfigureAwait(false);

        return Results.NoContent();
    }

    /// <summary>
    /// Debita a conta pelo boleto do documento.
    /// </summary>
    /// <remarks>
    /// Sem <c>Idempotency-Key</c> no cabecalho, ao contrario das movimentacoes: a chave e derivada
    /// do hash do arquivo. Pedir uma ao cliente aqui daria a ele a chance de mandar uma chave nova
    /// para o mesmo boleto — que e exatamente o cobrar duas vezes que a chave existe para impedir.
    /// <para>
    /// 201 na primeira vez e 200 no repeteco, como o resto do sistema.
    /// </para>
    /// </remarks>
    private static async Task<IResult> Pagar(
        Guid id,
        [FromHeader(Name = CabecalhoDeOperador)] string? operador,
        PagarDocumento pagar,
        CancellationToken cancelamento)
    {
        if (SemOperador(operador) is { } recusa)
        {
            return recusa;
        }

        var pagamento = await pagar.Executar(id, operador!, cancelamento).ConfigureAwait(false);

        return pagamento.Novo
            ? Results.Created($"/documentos/{id}", pagamento)
            : Results.Ok(pagamento);
    }

    private static IResult? SemOperador(string? operador) =>
        string.IsNullOrWhiteSpace(operador) || operador.Length > 100
            ? Results.Problem(
                title: $"Cabecalho {CabecalhoDeOperador} ausente ou longo demais",
                detail: "Toda operacao sobre documento precisa registrar quem a fez.",
                statusCode: StatusCodes.Status400BadRequest)
            : null;

    private static async Task<IResult> Detalhar(
        Guid id,
        ConsultarDocumento consultar,
        CancellationToken cancelamento) =>
        Results.Ok(await consultar.Executar(id, cancelamento).ConfigureAwait(false));
}
