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

        documentos.MapGet("/{id:guid}", Detalhar);
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
        if (string.IsNullOrWhiteSpace(operador) || operador.Length > 100)
        {
            return Results.Problem(
                title: $"Cabecalho {CabecalhoDeOperador} ausente ou longo demais",
                detail: "Todo documento precisa registrar quem enviou.",
                statusCode: StatusCodes.Status400BadRequest);
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
                new PedidoDeDocumento(contaId, arquivo.FileName, memoria.ToArray(), operador),
                cancelamento)
            .ConfigureAwait(false);

        // 202 na primeira vez e 200 no reenvio: quem chama distingue "entrou na fila agora"
        // de "ja estava la" sem comparar corpo.
        return recebido.Novo
            ? Results.Accepted($"/documentos/{recebido.Id}", recebido)
            : Results.Ok(recebido);
    }

    private static async Task<IResult> Detalhar(
        Guid id,
        ConsultarDocumento consultar,
        CancellationToken cancelamento) =>
        Results.Ok(await consultar.Executar(id, cancelamento).ConfigureAwait(false));
}
