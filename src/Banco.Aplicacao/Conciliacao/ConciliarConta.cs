using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Portas;

namespace Banco.Aplicacao.Conciliacao;

public sealed class ConciliarConta
{
    private readonly IConciliacaoDeLedger conciliacao;

    public ConciliarConta(IConciliacaoDeLedger conciliacao) => this.conciliacao = conciliacao;

    public async Task<ConciliacaoDaConta> Executar(Guid contaId, CancellationToken cancelamento) =>
        await conciliacao.DaConta(contaId, cancelamento).ConfigureAwait(false)
        ?? throw new ContaNaoEncontradaException($"Conta {contaId} nao encontrada.");
}
