using Banco.Dominio.Documentos;

namespace Banco.Aplicacao.Portas;

public interface IRepositorioDeDocumentos
{
    Task<Documento?> PorId(Guid id, CancellationToken cancelamento);

    /// <summary>
    /// O documento que este conteudo ja produziu nesta conta, se houver.
    /// </summary>
    /// <remarks>
    /// Por conta, e nao global: duas pessoas podem legitimamente receber o mesmo boleto —
    /// conta de luz de um imovel dividido, por exemplo — e cada uma paga a sua.
    /// </remarks>
    Task<Documento?> PorHash(Guid contaId, HashDoArquivo hash, CancellationToken cancelamento);

    void Adicionar(Documento documento);
}
