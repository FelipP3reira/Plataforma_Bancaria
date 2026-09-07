using Banco.Aplicacao.Contas;
using Banco.Dominio.Contas;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Banco.Api.Contas;

public sealed record MudancaDeEstadoHttp(string Motivo);

internal sealed class ValidadorDeMudancaDeEstado : AbstractValidator<MudancaDeEstadoHttp>
{
    public ValidadorDeMudancaDeEstado() =>
        RuleFor(pedido => pedido.Motivo)
            .NotEmpty().WithMessage("Diga por que a conta esta mudando de estado.")
            .MaximumLength(MudancaDeEstadoDaConta.TamanhoMaximoDoMotivo);
}

internal static class EndpointsDeEstadoDaConta
{
    private const string CabecalhoDeOperador = "X-Operador";

    public static void MapearEstadoDaConta(this IEndpointRouteBuilder rotas)
    {
        var conta = rotas.MapGroup("/contas/{id:guid}").WithTags("Estado da conta");

        conta.MapPost("/bloqueio", Bloquear);
        conta.MapPost("/desbloqueio", Desbloquear);
        conta.MapPost("/encerramento", Encerrar);
        conta.MapGet("/estados", Historico);
    }

    private static Task<IResult> Bloquear(
        Guid id,
        MudancaDeEstadoHttp? corpo,
        [FromHeader(Name = CabecalhoDeOperador)] string? operador,
        IValidator<MudancaDeEstadoHttp> validador,
        MudarEstadoDaConta mudar,
        CancellationToken cancelamento) =>
        Mudar(id, corpo, operador, validador, mudar.Bloquear, cancelamento);

    /// <remarks>
    /// POST, e nao DELETE do bloqueio, apesar de "remover o bloqueio" ser o que acontece.
    /// Desbloquear exige motivo — quem liberou uma conta bloqueada por suspeita precisa
    /// explicar tanto quanto quem bloqueou —, e corpo obrigatorio em DELETE e fragil: o
    /// proprio ASP.NET Core recusa inferir corpo nesse verbo, e proxies costumam descartar.
    /// </remarks>
    private static Task<IResult> Desbloquear(
        Guid id,
        MudancaDeEstadoHttp? corpo,
        [FromHeader(Name = CabecalhoDeOperador)] string? operador,
        IValidator<MudancaDeEstadoHttp> validador,
        MudarEstadoDaConta mudar,
        CancellationToken cancelamento) =>
        Mudar(id, corpo, operador, validador, mudar.Desbloquear, cancelamento);

    private static Task<IResult> Encerrar(
        Guid id,
        MudancaDeEstadoHttp? corpo,
        [FromHeader(Name = CabecalhoDeOperador)] string? operador,
        IValidator<MudancaDeEstadoHttp> validador,
        MudarEstadoDaConta mudar,
        CancellationToken cancelamento) =>
        Mudar(id, corpo, operador, validador, mudar.Encerrar, cancelamento);

    private static async Task<IResult> Historico(
        Guid id,
        ConsultarHistoricoDeEstado consultar,
        CancellationToken cancelamento) =>
        Results.Ok(await consultar.Executar(id, cancelamento).ConfigureAwait(false));

    private static async Task<IResult> Mudar(
        Guid id,
        MudancaDeEstadoHttp? corpo,
        string? operador,
        IValidator<MudancaDeEstadoHttp> validador,
        Func<PedidoDeMudancaDeEstado, CancellationToken, Task<EstadoDaContaMudou>> executar,
        CancellationToken cancelamento)
    {
        if (corpo is null)
        {
            return Results.Problem(
                title: "Corpo obrigatorio",
                detail: "Envie o motivo da mudanca de estado.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(operador) || operador.Length > 100)
        {
            return Results.Problem(
                title: $"Cabecalho {CabecalhoDeOperador} ausente ou longo demais",
                detail: $"Mudanca de estado exige {CabecalhoDeOperador}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var validacao = await validador.ValidateAsync(corpo, cancelamento).ConfigureAwait(false);
        if (!validacao.IsValid)
        {
            return Results.ValidationProblem(validacao.ToDictionary());
        }

        return Results.Ok(await executar(
            new PedidoDeMudancaDeEstado(id, corpo.Motivo, operador),
            cancelamento).ConfigureAwait(false));
    }
}
