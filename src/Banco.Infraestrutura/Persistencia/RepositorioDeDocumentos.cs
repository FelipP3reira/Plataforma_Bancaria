using Banco.Aplicacao.Portas;
using Banco.Dominio.Documentos;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infraestrutura.Persistencia;

public sealed class RepositorioDeDocumentos : IRepositorioDeDocumentos
{
    private readonly ContextoDoBanco contexto;

    public RepositorioDeDocumentos(ContextoDoBanco contexto) => this.contexto = contexto;

    public Task<Documento?> PorId(Guid id, CancellationToken cancelamento) =>
        contexto.Documentos.FirstOrDefaultAsync(documento => documento.Id == id, cancelamento);

    public Task<Documento?> PorHash(Guid contaId, HashDoArquivo hash, CancellationToken cancelamento) =>
        contexto.Documentos.FirstOrDefaultAsync(
            documento => documento.ContaId == contaId && documento.Hash == hash,
            cancelamento);

    public void Adicionar(Documento documento) => contexto.Documentos.Add(documento);
}
