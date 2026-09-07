using Banco.Dominio.Contas;
using Banco.Dominio.Ledger;

namespace Banco.Aplicacao.Portas;

public sealed record ResultadoDoLancamento(Lancamento Lancamento, bool Novo);

public interface IRepositorioDeContas
{
    Task<Conta?> PorId(Guid id, CancellationToken cancelamento);

    Task<Conta?> PorNumero(NumeroDaConta numero, CancellationToken cancelamento);

    /// <summary>
    /// Le a conta ja segurando a linha ate o fim da transacao.
    /// </summary>
    /// <remarks>
    /// Todo caminho que vai debitar ou creditar passa por aqui, e nunca por
    /// <see cref="PorId"/>. Movimentar e ler-decidir-gravar sobre a mesma linha: sem
    /// segurar a linha na leitura, duas requisicoes leem o mesmo saldo, as duas concluem
    /// que da, e as duas gravam.
    /// <para>
    /// Precisa estar dentro de uma transacao — a trava so vale ate o commit, e sem
    /// transacao aberta ela e liberada antes de a decisao virar gravacao.
    /// </para>
    /// </remarks>
    Task<Conta?> PorIdParaMovimentar(Guid id, CancellationToken cancelamento);

    void Adicionar(Conta conta);

    void Adicionar(Lancamento lancamento);

    /// <summary>
    /// Devolve o lancamento que a chave ja produziu nesta conta, se houver.
    /// </summary>
    Task<Lancamento?> PorChaveDeIdempotencia(
        Guid contaId,
        string chave,
        CancellationToken cancelamento);

    /// <summary>Proximo numero de conta, vindo de uma sequencia do banco.</summary>
    Task<NumeroDaConta> ProximoNumeroDeConta(CancellationToken cancelamento);
}
