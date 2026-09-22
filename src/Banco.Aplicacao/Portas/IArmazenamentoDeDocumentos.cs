using Banco.Dominio.Documentos;

namespace Banco.Aplicacao.Portas;

/// <summary>
/// Onde os bytes do documento ficam.
/// </summary>
/// <remarks>
/// Porta separada do repositorio porque sao dois meios com falhas diferentes: o banco pode
/// confirmar a linha e o disco recusar a escrita. Quem chama precisa gravar o arquivo antes
/// de confirmar a transacao — linha sem arquivo e um documento que nunca vai extrair, e
/// arquivo sem linha e so lixo que a faxina recolhe.
/// <para>
/// A implementacao de producao seria S3 ou Blob Storage; a interface e a mesma.
/// </para>
/// </remarks>
public interface IArmazenamentoDeDocumentos
{
    /// <summary>Grava e devolve o caminho relativo onde o arquivo ficou.</summary>
    Task<string> Guardar(HashDoArquivo hash, TipoDeArquivo tipo, Stream conteudo, CancellationToken cancelamento);

    Task<Stream?> Abrir(string caminhoRelativo, CancellationToken cancelamento);
}
