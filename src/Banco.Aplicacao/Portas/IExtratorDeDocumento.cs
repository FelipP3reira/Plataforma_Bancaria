using Banco.Dominio.Documentos;

namespace Banco.Aplicacao.Portas;

/// <param name="Conteudo">O texto lido do arquivo. Dado sensivel: nao vai para log.</param>
/// <param name="Confianca">Quanto o extrator confia no que leu, de 0 a 1.</param>
public sealed record TextoDoDocumento(string Conteudo, decimal Confianca);

/// <summary>
/// Quem le o arquivo e devolve texto.
/// </summary>
/// <remarks>
/// Porta, e nao chamada direta ao servico de IA, por dois motivos concretos: o provedor e
/// escolhido por configuracao, e trocar de provedor nao pode mexer no pipeline; e teste que
/// depende de servico externo paga por execucao e falha quando a internet cai.
/// <para>
/// Recebe <see cref="Stream"/> e nao <c>byte[]</c>: o arquivo pode ter dez megabytes, e quem
/// implementa contra HTTP repassa o fluxo sem materializar nada.
/// </para>
/// </remarks>
public interface IExtratorDeDocumento
{
    Task<TextoDoDocumento> Extrair(Stream conteudo, TipoDeArquivo tipo, CancellationToken cancelamento);
}
