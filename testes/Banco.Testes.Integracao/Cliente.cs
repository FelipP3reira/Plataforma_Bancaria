using System.Net.Http.Json;

namespace Banco.Testes.Integracao;

internal sealed record ContaAbertaHttp(
    Guid Id,
    string Numero,
    string Titular,
    decimal Saldo,
    DateTimeOffset AbertaEm);

internal sealed record LancamentoHttp(
    Guid Id,
    Guid ContaId,
    long Sequencia,
    string Tipo,
    decimal Valor,
    decimal Efeito,
    decimal SaldoDepois,
    string Descricao,
    string Origem,
    DateTimeOffset CriadoEm);

internal sealed record MudancaDeEstadoHttpResposta(
    Guid ContaId,
    string De,
    string Para,
    long Sequencia,
    string Motivo,
    string Origem,
    DateTimeOffset OcorridaEm);

internal sealed record DetalheDaContaHttp(
    Guid Id,
    string Numero,
    string Titular,
    string Estado,
    decimal Saldo,
    long Lancamentos,
    DateTimeOffset AbertaEm,
    DateTimeOffset AtualizadaEm);

internal sealed record TransferenciaHttpResposta(
    Guid Id,
    Guid ContaOrigemId,
    Guid ContaDestinoId,
    decimal Valor,
    string Descricao,
    string Origem,
    DateTimeOffset CriadaEm);

internal sealed record LinhaDoExtratoHttp(
    Guid Id,
    long Sequencia,
    string Tipo,
    decimal Valor,
    decimal Efeito,
    decimal SaldoDepois,
    string Descricao,
    string Origem,
    DateTimeOffset CriadoEm,
    Guid? TransferenciaId);

internal sealed record ExtratoHttp(
    Guid ContaId,
    string Numero,
    DateTimeOffset De,
    DateTimeOffset Ate,
    IReadOnlyList<LinhaDoExtratoHttp> Linhas,
    string? ProximaPagina);

internal sealed record ConciliacaoHttp(
    Guid ContaId,
    string Numero,
    decimal SaldoMaterializado,
    decimal SomaDoLedger,
    long UltimaSequenciaDaConta,
    long MaiorSequenciaNoLedger,
    long Lancamentos,
    long QuebrasNaCorrente,
    decimal Diferenca,
    bool Bate);

/// <summary>
/// Atalhos HTTP para as rotas que os testes usam o tempo todo.
/// </summary>
/// <remarks>
/// Existe para que cada teste diga o que esta provando, e nao como monta um cabecalho. As
/// chaves de idempotencia sao geradas por chamada de proposito: teste que quer provar
/// reenvio passa a chave explicitamente, e o resto nao pode esbarrar em idempotencia sem
/// querer.
/// </remarks>
internal static class Cliente
{
    public static async Task<ContaAbertaHttp> AbrirConta(this HttpClient cliente, string titular = "Ana Ribeiro")
    {
        var resposta = await cliente.PostAsJsonAsync("/contas", new { titular });
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<ContaAbertaHttp>(Pedidos.Json))!;
    }

    public static Task<HttpResponseMessage> Depositar(
        this HttpClient cliente,
        Guid contaId,
        decimal valor,
        string? chave = null,
        string descricao = "deposito",
        string operador = "operador:teste") =>
        cliente.Movimentar($"/contas/{contaId}/depositos", valor, chave, descricao, operador);

    public static Task<HttpResponseMessage> Sacar(
        this HttpClient cliente,
        Guid contaId,
        decimal valor,
        string? chave = null,
        string descricao = "saque",
        string operador = "operador:teste") =>
        cliente.Movimentar($"/contas/{contaId}/saques", valor, chave, descricao, operador);

    public static async Task<Guid> ContaCom(this HttpClient cliente, decimal saldo)
    {
        var conta = await cliente.AbrirConta();

        if (saldo > 0m)
        {
            (await cliente.Depositar(conta.Id, saldo)).EnsureSuccessStatusCode();
        }

        return conta.Id;
    }

    public static Task<DetalheDaContaHttp?> Detalhe(this HttpClient cliente, Guid contaId) =>
        cliente.GetFromJsonAsync<DetalheDaContaHttp>($"/contas/{contaId}", Pedidos.Json);

    public static Task<HttpResponseMessage> Bloquear(this HttpClient cliente, Guid contaId, string motivo) =>
        cliente.MudarEstado(HttpMethod.Post, $"/contas/{contaId}/bloqueio", motivo);

    public static Task<HttpResponseMessage> Desbloquear(this HttpClient cliente, Guid contaId, string motivo) =>
        cliente.MudarEstado(HttpMethod.Post, $"/contas/{contaId}/desbloqueio", motivo);

    public static Task<HttpResponseMessage> Encerrar(this HttpClient cliente, Guid contaId, string motivo) =>
        cliente.MudarEstado(HttpMethod.Post, $"/contas/{contaId}/encerramento", motivo);

    public static Task<IReadOnlyList<MudancaDeEstadoHttpResposta>?> HistoricoDeEstado(
        this HttpClient cliente,
        Guid contaId) =>
        cliente.GetFromJsonAsync<IReadOnlyList<MudancaDeEstadoHttpResposta>>(
            $"/contas/{contaId}/estados",
            Pedidos.Json);

    private static Task<HttpResponseMessage> MudarEstado(
        this HttpClient cliente,
        HttpMethod metodo,
        string rota,
        string motivo)
    {
        var pedido = new HttpRequestMessage(metodo, rota)
        {
            Content = JsonContent.Create(new { motivo }),
        };

        pedido.Headers.Add("X-Operador", "operador:teste");

        return cliente.SendAsync(pedido);
    }

    public static Task<HttpResponseMessage> ExtratoBruto(
        this HttpClient cliente,
        Guid contaId,
        string consulta = "") =>
        cliente.GetAsync($"/contas/{contaId}/extrato{consulta}");

    public static async Task<ExtratoHttp> Extrato(
        this HttpClient cliente,
        Guid contaId,
        string consulta = "")
    {
        var resposta = await cliente.ExtratoBruto(contaId, consulta);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<ExtratoHttp>(Pedidos.Json))!;
    }

    public static Task<ConciliacaoHttp?> Conciliacao(this HttpClient cliente, Guid contaId) =>
        cliente.GetFromJsonAsync<ConciliacaoHttp>($"/contas/{contaId}/conciliacao", Pedidos.Json);

    public static Task<HttpResponseMessage> Transferir(
        this HttpClient cliente,
        Guid origemId,
        Guid destinoId,
        decimal valor,
        string? chave = null,
        string descricao = "transferencia",
        string operador = "operador:teste")
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/contas/{origemId}/transferencias")
        {
            Content = JsonContent.Create(new { contaDestinoId = destinoId, valor, descricao }),
        };

        pedido.Headers.Add("Idempotency-Key", chave ?? Pedidos.ChaveNova());
        pedido.Headers.Add("X-Operador", operador);

        return cliente.SendAsync(pedido);
    }

    public static Task<TransferenciaHttpResposta?> Transferencia(this HttpResponseMessage resposta) =>
        resposta.Content.ReadFromJsonAsync<TransferenciaHttpResposta>(Pedidos.Json);

    public static Task<LancamentoHttp?> Lancamento(this HttpResponseMessage resposta) =>
        resposta.Content.ReadFromJsonAsync<LancamentoHttp>(Pedidos.Json);

    /// <summary>
    /// O titulo do ProblemDetails da recusa.
    /// </summary>
    /// <remarks>
    /// Os testes de concorrencia dependem dele: varias situacoes diferentes devolvem 409, e
    /// so o titulo distingue "nao havia saldo" de "duas gravacoes colidiram".
    /// </remarks>
    public static async Task<string?> TituloDoProblema(this HttpResponseMessage resposta)
    {
        ArgumentNullException.ThrowIfNull(resposta);

        var problema = await resposta.Content
            .ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(Pedidos.Json);

        return problema?.Title;
    }

    private static Task<HttpResponseMessage> Movimentar(
        this HttpClient cliente,
        string rota,
        decimal valor,
        string? chave,
        string descricao,
        string operador)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, rota)
        {
            Content = JsonContent.Create(new { valor, descricao }),
        };

        pedido.Headers.Add("Idempotency-Key", chave ?? Pedidos.ChaveNova());
        pedido.Headers.Add("X-Operador", operador);

        return cliente.SendAsync(pedido);
    }
}
