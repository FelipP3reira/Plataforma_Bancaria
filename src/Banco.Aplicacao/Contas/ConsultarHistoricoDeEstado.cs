using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Portas;

namespace Banco.Aplicacao.Contas;

/// <summary>
/// A trilha de estados da conta.
/// </summary>
/// <remarks>
/// Fica disponivel para conta encerrada tambem. Quem precisa auditar um encerramento
/// pergunta justamente depois que ele aconteceu.
/// </remarks>
public sealed class ConsultarHistoricoDeEstado
{
    private readonly IRepositorioDeContas contas;

    public ConsultarHistoricoDeEstado(IRepositorioDeContas contas) => this.contas = contas;

    public async Task<IReadOnlyList<EstadoDaContaMudou>> Executar(
        Guid contaId,
        CancellationToken cancelamento)
    {
        // Confere que a conta existe antes de devolver lista vazia: sem isso, id inventado
        // e conta que nunca mudou de estado dariam a mesma resposta.
        _ = await contas.PorId(contaId, cancelamento).ConfigureAwait(false)
            ?? throw new ContaNaoEncontradaException($"Conta {contaId} nao encontrada.");

        var mudancas = await contas.HistoricoDeEstado(contaId, cancelamento).ConfigureAwait(false);

        return [.. mudancas.Select(EstadoDaContaMudou.Da)];
    }
}
