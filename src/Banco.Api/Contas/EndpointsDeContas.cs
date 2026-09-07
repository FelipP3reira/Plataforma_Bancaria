using Banco.Aplicacao.Conciliacao;
using Banco.Aplicacao.Contas;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Banco.Api.Contas;

internal static class EndpointsDeContas
{
    private const string CabecalhoDeIdempotencia = "Idempotency-Key";
    private const string CabecalhoDeOperador = "X-Operador";

    public static void MapearContas(this IEndpointRouteBuilder rotas)
    {
        var contas = rotas.MapGroup("/contas").WithTags("Contas");

        contas.MapPost("/", Abrir);
        contas.MapGet("/{id:guid}", Detalhar);
        contas.MapGet("/{id:guid}/conciliacao", Conciliar);
        contas.MapPost("/{id:guid}/depositos", Depositar);
        contas.MapPost("/{id:guid}/saques", Sacar);
    }

    private static async Task<IResult> Abrir(
        PedidoDeAberturaHttp pedido,
        IValidator<PedidoDeAberturaHttp> validador,
        AbrirConta abrir,
        CancellationToken cancelamento)
    {
        var validacao = await validador.ValidateAsync(pedido, cancelamento).ConfigureAwait(false);
        if (!validacao.IsValid)
        {
            return Results.ValidationProblem(validacao.ToDictionary());
        }

        var aberta = await abrir
            .Executar(new PedidoDeAbertura(pedido.Titular), cancelamento)
            .ConfigureAwait(false);

        return Results.Created($"/contas/{aberta.Id}", aberta);
    }

    private static async Task<IResult> Detalhar(
        Guid id,
        ConsultarConta consultar,
        CancellationToken cancelamento) =>
        Results.Ok(await consultar.Executar(id, cancelamento).ConfigureAwait(false));

    /// <remarks>
    /// Rota de conferencia, e nao de conserto. Ela responde se o saldo materializado bate
    /// com o ledger; corrigir uma divergencia e decisao humana, porque a pergunta seguinte
    /// — qual dos dois esta certo — nao tem resposta automatica.
    /// </remarks>
    private static async Task<IResult> Conciliar(
        Guid id,
        ConciliarConta conciliar,
        CancellationToken cancelamento) =>
        Results.Ok(await conciliar.Executar(id, cancelamento).ConfigureAwait(false));

    private static Task<IResult> Depositar(
        Guid id,
        MovimentacaoHttp? corpo,
        [FromHeader(Name = CabecalhoDeIdempotencia)] string? chave,
        [FromHeader(Name = CabecalhoDeOperador)] string? operador,
        IValidator<MovimentacaoHttp> validador,
        MovimentarConta movimentar,
        CancellationToken cancelamento) =>
        Movimentar(id, corpo, chave, operador, validador, movimentar.Depositar, cancelamento);

    private static Task<IResult> Sacar(
        Guid id,
        MovimentacaoHttp? corpo,
        [FromHeader(Name = CabecalhoDeIdempotencia)] string? chave,
        [FromHeader(Name = CabecalhoDeOperador)] string? operador,
        IValidator<MovimentacaoHttp> validador,
        MovimentarConta movimentar,
        CancellationToken cancelamento) =>
        Movimentar(id, corpo, chave, operador, validador, movimentar.Sacar, cancelamento);

    /// <remarks>
    /// Deposito e saque diferem em uma linha — o metodo do caso de uso. Toda a borda é a
    /// mesma: os dois exigem chave de idempotencia, os dois exigem operador, os dois
    /// validam o mesmo corpo. Duplicar isso deixaria as duas rotas livres para divergirem
    /// em silencio, e a que divergisse seria a que aceita movimentacao sem chave.
    /// </remarks>
    private static async Task<IResult> Movimentar(
        Guid id,
        MovimentacaoHttp? corpo,
        string? chave,
        string? operador,
        IValidator<MovimentacaoHttp> validador,
        Func<PedidoDeMovimentacao, CancellationToken, Task<Banco.Aplicacao.Portas.ResultadoDoLancamento>> executar,
        CancellationToken cancelamento)
    {
        if (corpo is null)
        {
            return Results.Problem(
                title: "Corpo obrigatorio",
                detail: "Envie o valor e a descricao da movimentacao.",
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

        var resultado = await executar(
            new PedidoDeMovimentacao(id, corpo.Valor, corpo.Descricao, operador!, chave!),
            cancelamento).ConfigureAwait(false);

        var resposta = RespostaDeLancamento.De(resultado.Lancamento);

        // 200 no reenvio e 201 na primeira vez: o cliente distingue sem comparar corpo.
        return resultado.Novo
            ? Results.Created($"/contas/{id}/lancamentos/{resultado.Lancamento.Id}", resposta)
            : Results.Ok(resposta);
    }

    private static IResult? Recusar(string? valor, string cabecalho) =>
        string.IsNullOrWhiteSpace(valor) || valor.Length > 100
            ? Results.Problem(
                title: $"Cabecalho {cabecalho} ausente ou longo demais",
                detail: $"Toda movimentacao exige {cabecalho}.",
                statusCode: StatusCodes.Status400BadRequest)
            : null;
}
