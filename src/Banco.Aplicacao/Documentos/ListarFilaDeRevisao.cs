using Banco.Aplicacao.Portas;
using Banco.Dominio.Documentos;

namespace Banco.Aplicacao.Documentos;

/// <param name="MotivosDosCampos">
/// Por que cada campo merece atencao, do menos confiavel para o mais. E o que a tela precisa para
/// abrir o documento no campo certo em vez de mostrar tudo igual.
/// </param>
public sealed record DocumentoParaRevisar(
    Guid Id,
    Guid ContaId,
    string NomeOriginal,
    decimal Confianca,
    decimal? ConfiancaDoTexto,
    DateTimeOffset RecebidoEm,
    IReadOnlyList<string> MotivosDosCampos);

/// <summary>
/// A fila de quem precisa de olho humano.
/// </summary>
/// <remarks>
/// Sem paginacao por marcador, ao contrario do extrato, e a diferenca nao e descuido. Extrato
/// cresce para sempre e e para navegar; fila de revisao e para <b>esvaziar</b>. Se ela passar do
/// teto configurado, o que falta nao e mais uma pagina — e gente, ou uma regua de confianca mais
/// solta.
/// <para>
/// Nao devolve o conteudo extraido nem os valores dos campos. A lista aparece em tela aberta o dia
/// inteiro, e valor de boleto e CNPJ nao precisam ficar ali: quem for revisar abre o documento.
/// </para>
/// </remarks>
public sealed class ListarFilaDeRevisao
{
    private readonly IRepositorioDeDocumentos documentos;
    private readonly OpcoesDoPipeline opcoes;

    public ListarFilaDeRevisao(IRepositorioDeDocumentos documentos, OpcoesDoPipeline opcoes)
    {
        this.documentos = documentos;
        this.opcoes = opcoes;
    }

    public async Task<IReadOnlyList<DocumentoParaRevisar>> Executar(int? limite, CancellationToken cancelamento)
    {
        // O limite pedido e aparado no teto em vez de recusado: quem pede 5000 quer "tudo", e
        // devolver o maximo atende melhor do que um 400.
        var quantidade = Math.Clamp(limite ?? opcoes.TamanhoMaximoDaRevisao, 1, opcoes.TamanhoMaximoDaRevisao);

        var fila = await documentos.ParaRevisao(quantidade, cancelamento).ConfigureAwait(false);

        return [.. fila.Select(Resumir)];
    }

    private static DocumentoParaRevisar Resumir(Documento documento) =>
        new(
            documento.Id,
            documento.ContaId,
            documento.NomeOriginal,
            documento.Confianca ?? 0m,
            documento.ConfiancaDoTexto,
            documento.RecebidoEm,
            [.. documento.Campos
                .OrderBy(campo => campo.Confianca)
                .Select(campo => $"{campo.Nome}: {campo.Confianca:0.00}" + (campo.Observacao is { } porque ? $" — {porque}" : string.Empty))]);
}
