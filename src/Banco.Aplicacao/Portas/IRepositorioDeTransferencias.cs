using Banco.Dominio.Transferencias;

namespace Banco.Aplicacao.Portas;

public interface IRepositorioDeTransferencias
{
    Task<Transferencia?> PorId(Guid id, CancellationToken cancelamento);

    /// <summary>
    /// A transferencia que esta chave ja produziu saindo desta conta, se houver.
    /// </summary>
    Task<Transferencia?> PorChaveDeIdempotencia(
        Guid contaOrigemId,
        string chave,
        CancellationToken cancelamento);

    void Adicionar(Transferencia transferencia);
}
