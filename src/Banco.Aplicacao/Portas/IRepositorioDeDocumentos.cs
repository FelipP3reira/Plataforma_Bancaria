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

    /// <summary>
    /// O proximo documento da fila, travado para quem chamou, ou nulo se a fila esta vazia.
    /// </summary>
    /// <remarks>
    /// A trava e obrigatoria: sem ela dois workers leem o mesmo documento e a extracao —
    /// que e paga por chamada — sai duas vezes. Quem chama precisa estar dentro de uma
    /// transacao, porque a trava sobrevive exatamente pelo tempo dela.
    /// </remarks>
    Task<Documento?> ProximoDaFila(CancellationToken cancelamento);

    /// <summary>
    /// Os documentos esperando olho humano, os menos confiaveis primeiro.
    /// </summary>
    /// <remarks>
    /// Ordenado pela confianca e nao pela chegada: a fila de revisao nao e por ordem de entrada, e
    /// por gravidade. O documento cujo valor impresso nao bate com o codigo de barras precisa ser
    /// visto antes do que so tem o CNPJ ambiguo.
    /// </remarks>
    Task<IReadOnlyList<Documento>> ParaRevisao(int quantidade, CancellationToken cancelamento);

    /// <summary>
    /// Os documentos cuja reserva venceu: o worker que os pegou nao voltou.
    /// </summary>
    Task<IReadOnlyList<Documento>> ComLeaseVencido(
        DateTimeOffset agora,
        int quantidade,
        CancellationToken cancelamento);
}
