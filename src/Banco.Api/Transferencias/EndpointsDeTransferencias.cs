using Banco.Aplicacao.Transferencias;
using Banco.Dominio.Comum;
using Banco.Dominio.Ledger;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Banco.Api.Transferencias;

public sealed record TransferenciaHttp(Guid ContaDestinoId, decimal Valor, string Descricao);

public sealed record RespostaDeTransferencia(
    Guid Id,
    Guid ContaOrigemId,
    Guid ContaDestinoId,
    decimal Valor,
    string Descricao,
    string Origem,
    DateTimeOffset CriadaEm)
{
    public static RespostaDeTransferencia De(Dominio.Transferencias.Transferencia transferencia)
    {
        ArgumentNullException.ThrowIfNull(transferencia);

        return new RespostaDeTransferencia(
            transferencia.Id,
            transferencia.ContaOrigemId,
            transferencia.ContaDestinoId,
            transferencia.Valor.Valor,
            transferencia.Descricao,
            transferencia.Origem,
            transferencia.CriadaEm);
    }
}

internal sealed class ValidadorDeTransferencia : AbstractValidator<TransferenciaHttp>
{
    public ValidadorDeTransferencia()
    {
        RuleFor(pedido => pedido.ContaDestinoId).NotEmpty().WithMessage("Informe a conta de destino.");

        RuleFor(pedido => pedido.Valor)
            .GreaterThan(0m).WithMessage("O valor precisa ser maior que zero.")
            .Must(valor => decimal.Round(valor, Dinheiro.CasasDecimais) == valor)
            .WithMessage($"O valor nao pode ter mais de {Dinheiro.CasasDecimais} casas decimais.");

        RuleFor(pedido => pedido.Descricao)
            .NotEmpty().WithMessage("Descreva a transferencia.")
            .MaximumLength(Lancamento.TamanhoMaximoDaDescricao);
    }
}

internal static class EndpointsDeTransferencias
{
    private const string CabecalhoDeIdempotencia = "Idempotency-Key";
    private const string CabecalhoDeOperador = "X-Operador";

    public static void MapearTransferencias(this IEndpointRouteBuilder rotas)
    {
        rotas.MapPost("/contas/{id:guid}/transferencias", Transferir).WithTags("Transferencias");
        rotas.MapGet("/transferencias/{id:guid}", Detalhar).WithTags("Transferencias");
    }

    /// <remarks>
    /// Pendurada na conta de origem, e nao numa rota solta: transferencia e uma operacao que
    /// sai de uma conta, e e o dono dela que manda a chave de idempotencia.
    /// </remarks>
    private static async Task<IResult> Transferir(
        Guid id,
        TransferenciaHttp? corpo,
        [FromHeader(Name = CabecalhoDeIdempotencia)] string? chave,
        [FromHeader(Name = CabecalhoDeOperador)] string? operador,
        IValidator<TransferenciaHttp> validador,
        TransferirEntreContas transferir,
        CancellationToken cancelamento)
    {
        if (corpo is null)
        {
            return Results.Problem(
                title: "Corpo obrigatorio",
                detail: "Envie a conta de destino, o valor e a descricao.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (Recusar(chave, CabecalhoDeIdempotencia) is { } semChave)
        {
            return semChave;
        }

        if (Recusar(operador, CabecalhoDeOperador) is { } semOperador)
        {
            return semOperador;
        }

        var validacao = await validador.ValidateAsync(corpo, cancelamento).ConfigureAwait(false);
        if (!validacao.IsValid)
        {
            return Results.ValidationProblem(validacao.ToDictionary());
        }

        var resultado = await transferir.Executar(
            new PedidoDeTransferencia(
                id,
                corpo.ContaDestinoId,
                corpo.Valor,
                corpo.Descricao,
                operador!,
                chave!),
            cancelamento).ConfigureAwait(false);

        var resposta = RespostaDeTransferencia.De(resultado.Transferencia);

        // 200 no reenvio e 201 na primeira vez: o cliente distingue sem comparar corpo.
        return resultado.Nova
            ? Results.Created($"/transferencias/{resultado.Transferencia.Id}", resposta)
            : Results.Ok(resposta);
    }

    private static async Task<IResult> Detalhar(
        Guid id,
        ConsultarTransferencia consultar,
        CancellationToken cancelamento) =>
        Results.Ok(RespostaDeTransferencia.De(
            await consultar.Executar(id, cancelamento).ConfigureAwait(false)));

    private static IResult? Recusar(string? valor, string cabecalho) =>
        string.IsNullOrWhiteSpace(valor) || valor.Length > 100
            ? Results.Problem(
                title: $"Cabecalho {cabecalho} ausente ou longo demais",
                detail: $"Toda movimentacao exige {cabecalho}.",
                statusCode: StatusCodes.Status400BadRequest)
            : null;
}
