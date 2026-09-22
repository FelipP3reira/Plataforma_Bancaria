using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Portas;
using Banco.Dominio.Documentos;

namespace Banco.Aplicacao.Documentos;

/// <param name="Confianca">De 0 a 1. E ela que decide se o campo precisa de olho humano.</param>
/// <param name="ValorFinal">O que vale: o corrigido quando existe, o lido quando nao.</param>
public sealed record CampoDoDocumentoLido(
    string Nome,
    string ValorLido,
    string ValorFinal,
    decimal Confianca,
    string Origem,
    string? Observacao,
    string? CorrigidoPor,
    DateTimeOffset? CorrigidoEm);

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
    DateTimeOffset AtualizadoEm,
    int Tentativas,
    string? UltimoErro,
    decimal? Confianca,
    decimal? ConfiancaDoTexto,
    DateTimeOffset? ExtraidoEm,
    string? ConteudoExtraido,
    Guid? LancamentoDoPagamentoId,
    DateTimeOffset? PagoEm,
    IReadOnlyList<CampoDoDocumentoLido> Campos);

/// <remarks>
/// <c>LeaseAte</c> nao aparece na resposta: e mecanica interna da fila, e quem consulta o
/// documento nao tem o que fazer com o prazo de reserva de um worker.
/// </remarks>
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
            documento.AtualizadoEm,
            documento.Tentativas,
            documento.UltimoErro,
            documento.Confianca,
            documento.ConfiancaDoTexto,
            documento.ExtraidoEm,
            documento.ConteudoExtraido,
            documento.LancamentoDoPagamentoId,
            documento.PagoEm,
            [.. documento.Campos
                .OrderBy(campo => campo.Nome)
                .Select(campo => new CampoDoDocumentoLido(
                    campo.Nome.ToString(),
                    campo.ValorLido,
                    campo.ValorFinal,
                    campo.Confianca,
                    campo.Origem.ToString(),
                    campo.Observacao,
                    campo.CorrigidoPor,
                    campo.CorrigidoEm))]);
    }
}
