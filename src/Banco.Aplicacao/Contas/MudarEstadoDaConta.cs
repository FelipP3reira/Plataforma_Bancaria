using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Portas;
using Banco.Dominio.Contas;

namespace Banco.Aplicacao.Contas;

public sealed record PedidoDeMudancaDeEstado(Guid ContaId, string Motivo, string Origem);

public sealed record EstadoDaContaMudou(
    Guid ContaId,
    EstadoDaConta De,
    EstadoDaConta Para,
    long Sequencia,
    string Motivo,
    string Origem,
    DateTimeOffset OcorridaEm)
{
    public static EstadoDaContaMudou Da(MudancaDeEstadoDaConta mudanca)
    {
        ArgumentNullException.ThrowIfNull(mudanca);

        return new EstadoDaContaMudou(
            mudanca.ContaId,
            mudanca.De,
            mudanca.Para,
            mudanca.Sequencia,
            mudanca.Motivo,
            mudanca.Origem,
            mudanca.OcorridaEm);
    }
}

/// <summary>
/// Bloqueia, desbloqueia e encerra.
/// </summary>
/// <remarks>
/// Passa pela mesma trava das movimentacoes, e nao por uma leitura solta. Bloquear enquanto
/// um saque esta no meio do caminho e exatamente o momento em que o bloqueio precisa
/// funcionar: sem a trava, o saque leria a conta ativa, o bloqueio gravaria, e o saque
/// gravaria depois — dinheiro saindo de uma conta que ja estava bloqueada.
/// <para>
/// Sem chave de idempotencia: a operacao e idempotente por natureza. Bloquear duas vezes a
/// segunda ja e recusada pela maquina de estados, porque Bloqueada nao vai para Bloqueada.
/// </para>
/// </remarks>
public sealed class MudarEstadoDaConta
{
    private readonly IRepositorioDeContas contas;
    private readonly IUnidadeDeTrabalho unidade;
    private readonly TimeProvider relogio;

    public MudarEstadoDaConta(
        IRepositorioDeContas contas,
        IUnidadeDeTrabalho unidade,
        TimeProvider relogio)
    {
        this.contas = contas;
        this.unidade = unidade;
        this.relogio = relogio;
    }

    public Task<EstadoDaContaMudou> Bloquear(PedidoDeMudancaDeEstado pedido, CancellationToken cancelamento) =>
        Executar(pedido, (conta, motivo, origem, agora) => conta.Bloquear(motivo, origem, agora), cancelamento);

    public Task<EstadoDaContaMudou> Desbloquear(PedidoDeMudancaDeEstado pedido, CancellationToken cancelamento) =>
        Executar(pedido, (conta, motivo, origem, agora) => conta.Desbloquear(motivo, origem, agora), cancelamento);

    public Task<EstadoDaContaMudou> Encerrar(PedidoDeMudancaDeEstado pedido, CancellationToken cancelamento) =>
        Executar(pedido, (conta, motivo, origem, agora) => conta.Encerrar(motivo, origem, agora), cancelamento);

    private async Task<EstadoDaContaMudou> Executar(
        PedidoDeMudancaDeEstado pedido,
        Func<Conta, string, string, DateTimeOffset, MudancaDeEstadoDaConta> mudar,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);

        await using var transacao = await unidade.Abrir(cancelamento).ConfigureAwait(false);

        var conta = await contas.PorIdParaMovimentar(pedido.ContaId, cancelamento).ConfigureAwait(false)
            ?? throw new ContaNaoEncontradaException($"Conta {pedido.ContaId} nao encontrada.");

        var mudanca = mudar(conta, pedido.Motivo, pedido.Origem, relogio.GetUtcNow());

        contas.Adicionar(mudanca);
        await unidade.Salvar(cancelamento).ConfigureAwait(false);
        await transacao.Confirmar(cancelamento).ConfigureAwait(false);

        return EstadoDaContaMudou.Da(mudanca);
    }
}
