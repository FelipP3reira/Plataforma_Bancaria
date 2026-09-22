using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Portas;
using Banco.Dominio.Documentos.Boletos;
using Banco.Dominio.Erros;

namespace Banco.Aplicacao.Documentos;

/// <param name="Correcoes">
/// Nome do campo para o valor corrigido. Campo que nao aparece aqui fica como a maquina leu — a
/// revisao e conferir e consertar o que estiver errado, nao redigitar tudo.
/// </param>
public sealed record PedidoDeRevisao(
    IReadOnlyDictionary<string, string> Correcoes,
    string Revisor);

/// <summary>
/// Registra a conferencia humana de um documento.
/// </summary>
/// <remarks>
/// Fecha a revisao mesmo sem correcao nenhuma: "olhei e esta certo" e um resultado, e o mais
/// comum. Exigir correcao para poder confirmar levaria quem revisa a inventar uma.
/// </remarks>
public sealed class RevisarDocumento
{
    private readonly IRepositorioDeDocumentos documentos;
    private readonly IUnidadeDeTrabalho unidade;
    private readonly TimeProvider relogio;

    public RevisarDocumento(
        IRepositorioDeDocumentos documentos,
        IUnidadeDeTrabalho unidade,
        TimeProvider relogio)
    {
        this.documentos = documentos;
        this.unidade = unidade;
        this.relogio = relogio;
    }

    public async Task Executar(Guid id, PedidoDeRevisao pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);

        var documento = await documentos.PorId(id, cancelamento).ConfigureAwait(false)
            ?? throw new DocumentoNaoEncontradoException($"Documento {id} nao encontrado.");

        documento.Revisar(Nomear(pedido.Correcoes), pedido.Revisor, relogio.GetUtcNow());

        await unidade.Salvar(cancelamento).ConfigureAwait(false);
    }

    /// <remarks>
    /// O nome do campo chega como texto porque vem de JSON. Nome desconhecido e recusa explicita e
    /// nao silencio: correcao ignorada por causa de um erro de digitacao no nome faria quem revisou
    /// acreditar que consertou o valor.
    /// </remarks>
    private static Dictionary<NomeDoCampo, string> Nomear(IReadOnlyDictionary<string, string> correcoes)
    {
        var nomeados = new Dictionary<NomeDoCampo, string>();

        foreach (var (nome, valor) in correcoes)
        {
            if (!Enum.TryParse<NomeDoCampo>(nome, ignoreCase: true, out var campo))
            {
                throw new BoletoInvalidoException(
                    $"Campo desconhecido: '{nome}'. Os campos sao "
                    + string.Join(", ", Enum.GetNames<NomeDoCampo>()) + ".");
            }

            nomeados[campo] = valor;
        }

        return nomeados;
    }
}
