using Banco.Aplicacao.Erros;
using Banco.Dominio.Erros;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Banco.Api.Erros;

/// <summary>
/// Traduz violacao de regra em resposta HTTP. So trata o que e erro do pedido — o resto
/// sobe e vira 500, porque falha nossa nao pode virar 400 e sumir do radar.
/// </summary>
public sealed partial class TratamentoDeErrosDeDominio : IExceptionHandler
{
    private readonly IProblemDetailsService detalhes;
    private readonly ILogger<TratamentoDeErrosDeDominio> registro;

    public TratamentoDeErrosDeDominio(IProblemDetailsService detalhes, ILogger<TratamentoDeErrosDeDominio> registro)
    {
        this.detalhes = detalhes;
        this.registro = registro;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (Traduzir(exception) is not { } traducao)
        {
            return false;
        }

        var (status, titulo) = traducao;

        httpContext.Response.StatusCode = status;

        // Sem isto o erro de dominio nao deixa rastro nenhum: o registro de requisicao roda
        // por fora daqui e so enxerga o codigo de status, sem dizer o que houve.
        RegistrarRecusa(status, titulo, exception.Message);

        return await detalhes.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = titulo,
                Detail = exception.Message,
            },
        }).ConfigureAwait(false);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Pedido recusado com {Status} ({Titulo}): {Motivo}")]
    private partial void RegistrarRecusa(int status, string titulo, string motivo);

    private static (int Status, string Titulo)? Traduzir(Exception excecao) => excecao switch
    {
        ContaNaoEncontradaException => (StatusCodes.Status404NotFound, "Conta nao encontrada"),
        TransferenciaNaoEncontradaException =>
            (StatusCodes.Status404NotFound, "Transferencia nao encontrada"),

        // 409 e nao 400: o pedido esta bem formado, o que impede e o estado da conta.
        SaldoInsuficienteException => (StatusCodes.Status409Conflict, "Saldo insuficiente"),
        ChaveDeIdempotenciaReutilizadaException =>
            (StatusCodes.Status409Conflict, "Chave de idempotencia reutilizada"),
        LedgerInconsistenteException => (StatusCodes.Status409Conflict, "Ledger inconsistente"),

        // O pedido esta certo; o que impede e o estado em que a conta esta.
        ContaBloqueadaException => (StatusCodes.Status409Conflict, "Conta nao movimenta"),
        TransicaoInvalidaException => (StatusCodes.Status409Conflict, "Mudanca de estado invalida"),

        // Duas gravacoes concorrentes tentaram a mesma posicao do ledger. Se isto aparecer,
        // a trava pessimista falhou em algum caminho — e o indice unico segurou.
        DbUpdateException erro when EhViolacaoDeUnicidade(erro) =>
            (StatusCodes.Status409Conflict, "Conflito de gravacao — tente de novo"),

        DominioException => (StatusCodes.Status400BadRequest, "Pedido invalido"),
        _ => null,
    };

    private static bool EhViolacaoDeUnicidade(DbUpdateException erro) =>
        erro.InnerException is Microsoft.Data.SqlClient.SqlException falha
        && falha.Number is 2601 or 2627;
}
