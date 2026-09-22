using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Portas;

namespace Banco.Aplicacao.Documentos;

/// <summary>
/// Devolve a fila um documento que esgotou as tentativas.
/// </summary>
/// <remarks>
/// A saida da dead-letter, e ela e manual de proposito. O que falhou tres vezes sozinho
/// falharia na quarta pelo mesmo motivo — reenfileirar automaticamente so gastaria chamada
/// paga de extracao em laco. Alguem olha, arruma a causa e manda de novo.
/// </remarks>
public sealed class ReenfileirarDocumento
{
    private readonly IRepositorioDeDocumentos documentos;
    private readonly IUnidadeDeTrabalho unidade;
    private readonly TimeProvider relogio;

    public ReenfileirarDocumento(
        IRepositorioDeDocumentos documentos,
        IUnidadeDeTrabalho unidade,
        TimeProvider relogio)
    {
        this.documentos = documentos;
        this.unidade = unidade;
        this.relogio = relogio;
    }

    public async Task Executar(Guid id, CancellationToken cancelamento)
    {
        var documento = await documentos.PorId(id, cancelamento).ConfigureAwait(false)
            ?? throw new DocumentoNaoEncontradoException($"Documento {id} nao encontrado.");

        // Quem valida o estado e a maquina de transicoes, e nao um if aqui: documento que
        // esta extraindo agora nao pode voltar para a fila, e essa regra ja esta escrita num
        // lugar so.
        documento.Reenfileirar(relogio.GetUtcNow());

        await unidade.Salvar(cancelamento).ConfigureAwait(false);
    }
}
