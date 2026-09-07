using Banco.Aplicacao.Portas;
using Banco.Dominio.Transferencias;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infraestrutura.Persistencia;

public sealed class RepositorioDeTransferencias : IRepositorioDeTransferencias
{
    private readonly ContextoDoBanco contexto;

    public RepositorioDeTransferencias(ContextoDoBanco contexto) => this.contexto = contexto;

    public Task<Transferencia?> PorId(Guid id, CancellationToken cancelamento) =>
        contexto.Transferencias
            .AsNoTracking()
            .FirstOrDefaultAsync(transferencia => transferencia.Id == id, cancelamento);

    /// <remarks>
    /// Sem rastreamento: e uma consulta de decisao, e nao o comeco de uma edicao.
    /// Transferencia gravada nao muda mais.
    /// </remarks>
    public Task<Transferencia?> PorChaveDeIdempotencia(
        Guid contaOrigemId,
        string chave,
        CancellationToken cancelamento) =>
        contexto.Transferencias
            .AsNoTracking()
            .FirstOrDefaultAsync(
                transferencia => transferencia.ContaOrigemId == contaOrigemId
                    && transferencia.ChaveIdempotencia == chave,
                cancelamento);

    public void Adicionar(Transferencia transferencia) => contexto.Transferencias.Add(transferencia);
}
