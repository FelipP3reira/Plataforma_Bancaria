using Banco.Aplicacao.Extrato;

namespace Banco.Api.Contas;

internal static class EndpointsDeExtrato
{
    /// <remarks>
    /// Rota de leitura, sem chave de idempotencia e sem operador: consultar nao move
    /// dinheiro, e exigir chave em GET so ensinaria o cliente a gerar chave a esmo.
    /// </remarks>
    public static void MapearExtrato(this IEndpointRouteBuilder rotas) =>
        rotas.MapGet("/contas/{id:guid}/extrato", Extrato).WithTags("Contas");

    private static async Task<IResult> Extrato(
        Guid id,
        DateTimeOffset? de,
        DateTimeOffset? ate,
        int? tamanho,
        string? pagina,
        ConsultarExtrato consultar,
        CancellationToken cancelamento) =>
        Results.Ok(await consultar
            .Executar(new PedidoDeExtrato(id, de, ate, tamanho, pagina), cancelamento)
            .ConfigureAwait(false));
}
