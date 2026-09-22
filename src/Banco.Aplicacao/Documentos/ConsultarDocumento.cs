using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Portas;
using Banco.Dominio.Documentos;

namespace Banco.Aplicacao.Documentos;

public sealed record DetalheDoDocumento(
    Guid Id,
    Guid ContaId,
    string NomeOriginal,
    TipoDeArquivo Tipo,
    long TamanhoEmBytes,
    string Hash,
    EstadoDoDocumento Estado,
    string Origem,
    DateTimeOffset RecebidoEm,
    DateTimeOffset AtualizadoEm);

public sealed class ConsultarDocumento
{
    private readonly IRepositorioDeDocumentos documentos;

    public ConsultarDocumento(IRepositorioDeDocumentos documentos) => this.documentos = documentos;

    public async Task<DetalheDoDocumento> Executar(Guid id, CancellationToken cancelamento)
    {
        var documento = await documentos.PorId(id, cancelamento).ConfigureAwait(false)
            ?? throw new DocumentoNaoEncontradoException($"Documento {id} nao encontrado.");

        return new DetalheDoDocumento(
            documento.Id,
            documento.ContaId,
            documento.NomeOriginal,
            documento.Tipo,
            documento.TamanhoEmBytes,
            documento.Hash.Texto,
            documento.Estado,
            documento.Origem,
            documento.RecebidoEm,
            documento.AtualizadoEm);
    }
}
