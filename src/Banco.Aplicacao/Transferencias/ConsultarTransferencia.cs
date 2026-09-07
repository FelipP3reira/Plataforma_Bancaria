using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Portas;
using Banco.Dominio.Transferencias;

namespace Banco.Aplicacao.Transferencias;

public sealed class ConsultarTransferencia
{
    private readonly IRepositorioDeTransferencias transferencias;

    public ConsultarTransferencia(IRepositorioDeTransferencias transferencias) =>
        this.transferencias = transferencias;

    public async Task<Transferencia> Executar(Guid id, CancellationToken cancelamento) =>
        await transferencias.PorId(id, cancelamento).ConfigureAwait(false)
        ?? throw new TransferenciaNaoEncontradaException($"Transferencia {id} nao encontrada.");
}
