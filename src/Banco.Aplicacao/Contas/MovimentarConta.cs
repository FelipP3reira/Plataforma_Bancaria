using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Portas;
using Banco.Dominio.Comum;
using Banco.Dominio.Contas;
using Banco.Dominio.Ledger;

namespace Banco.Aplicacao.Contas;

public sealed record PedidoDeMovimentacao(
    Guid ContaId,
    decimal Valor,
    string Descricao,
    string Origem,
    string ChaveIdempotencia);

/// <summary>
/// Deposito e saque: um lancamento numa conta so.
/// </summary>
/// <remarks>
/// O caminho inteiro roda dentro de uma transacao, e a leitura da conta segura a linha ate
/// o commit. Sem isso, dois saques simultaneos leem o mesmo saldo, os dois concluem que da,
/// e os dois gravam — a conta fica devendo dinheiro que nunca teve.
/// </remarks>
public sealed class MovimentarConta
{
    private readonly IRepositorioDeContas contas;
    private readonly IUnidadeDeTrabalho unidade;
    private readonly TimeProvider relogio;

    public MovimentarConta(
        IRepositorioDeContas contas,
        IUnidadeDeTrabalho unidade,
        TimeProvider relogio)
    {
        this.contas = contas;
        this.unidade = unidade;
        this.relogio = relogio;
    }

    public Task<ResultadoDoLancamento> Depositar(PedidoDeMovimentacao pedido, CancellationToken cancelamento) =>
        Executar(pedido, TipoDeLancamento.Credito, cancelamento);

    public Task<ResultadoDoLancamento> Sacar(PedidoDeMovimentacao pedido, CancellationToken cancelamento) =>
        Executar(pedido, TipoDeLancamento.Debito, cancelamento);

    private async Task<ResultadoDoLancamento> Executar(
        PedidoDeMovimentacao pedido,
        TipoDeLancamento tipo,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);

        await using var transacao = await unidade.Abrir(cancelamento).ConfigureAwait(false);

        var conta = await contas.PorIdParaMovimentar(pedido.ContaId, cancelamento).ConfigureAwait(false)
            ?? throw new ContaNaoEncontradaException($"Conta {pedido.ContaId} nao encontrada.");

        // A conferencia de reenvio acontece DEPOIS de pegar a trava. Antes dela, duas
        // requisicoes com a mesma chave passariam juntas pela consulta, as duas achariam
        // que sao a primeira, e o indice unico so recusaria uma delas — com a outra ja
        // tendo movido o saldo. Com a trava, a segunda so consulta quando a primeira
        // terminou, e ai encontra o lancamento que ja existe.
        if (await contas
                .PorChaveDeIdempotencia(conta.Id, pedido.ChaveIdempotencia, cancelamento)
                .ConfigureAwait(false) is { } existente)
        {
            GarantirQueEhOMesmoPedido(existente, pedido, tipo);

            await transacao.Confirmar(cancelamento).ConfigureAwait(false);
            return new ResultadoDoLancamento(existente, Novo: false);
        }

        var lancamento = Registrar(conta, tipo, pedido);

        contas.Adicionar(lancamento);
        await unidade.Salvar(cancelamento).ConfigureAwait(false);
        await transacao.Confirmar(cancelamento).ConfigureAwait(false);

        return new ResultadoDoLancamento(lancamento, Novo: true);
    }

    private Lancamento Registrar(Conta conta, TipoDeLancamento tipo, PedidoDeMovimentacao pedido)
    {
        var lancamento = new PedidoDeLancamento(
            Dinheiro.De(pedido.Valor),
            pedido.Descricao,
            pedido.Origem,
            pedido.ChaveIdempotencia,
            relogio.GetUtcNow());

        return tipo == TipoDeLancamento.Credito
            ? conta.Creditar(lancamento)
            : conta.Debitar(lancamento);
    }

    /// <remarks>
    /// Mesma chave com conteudo diferente nao e reenvio: e o cliente reaproveitando uma
    /// chave por engano. Devolver o lancamento antigo confirmaria uma operacao que ninguem
    /// pediu, entao a resposta certa e recusar.
    /// </remarks>
    private static void GarantirQueEhOMesmoPedido(
        Lancamento existente,
        PedidoDeMovimentacao pedido,
        TipoDeLancamento tipo)
    {
        if (existente.Tipo != tipo || existente.Valor.Valor != pedido.Valor)
        {
            throw new ChaveDeIdempotenciaReutilizadaException(
                $"A chave '{pedido.ChaveIdempotencia}' ja foi usada nesta conta para outra operacao.");
        }
    }
}
