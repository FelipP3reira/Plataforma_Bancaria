using Banco.Aplicacao.Conciliacao;

namespace Banco.Aplicacao.Portas;

/// <summary>
/// Confere o saldo materializado contra o ledger.
/// </summary>
/// <remarks>
/// Roda no banco, e nao em memoria: trazer o ledger inteiro de uma conta para somar aqui
/// seria justamente o custo que o saldo materializado existe para evitar.
/// </remarks>
public interface IConciliacaoDeLedger
{
    Task<ConciliacaoDaConta?> DaConta(Guid contaId, CancellationToken cancelamento);
}
