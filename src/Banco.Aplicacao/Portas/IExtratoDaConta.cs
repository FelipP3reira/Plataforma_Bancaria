using Banco.Aplicacao.Extrato;

namespace Banco.Aplicacao.Portas;

/// <summary>
/// Leitura do ledger para extrato, fora do agregado.
/// </summary>
/// <remarks>
/// A conta nao carrega os lancamentos, e este e o motivo de ela poder nao carregar: quem
/// precisa da lista pergunta aqui. Carregar o ledger dentro do agregado faria toda
/// movimentacao pagar a leitura da conta inteira para gravar uma linha.
/// </remarks>
public interface IExtratoDaConta
{
    /// <summary>
    /// As linhas do periodo, da mais recente para a mais antiga, no maximo
    /// <c>Tamanho + 1</c>.
    /// </summary>
    /// <remarks>
    /// A linha a mais e como o chamador sabe que existe proxima pagina sem contar o
    /// periodo inteiro. Descartar essa linha e do chamador — a porta devolve o que leu.
    /// </remarks>
    Task<IReadOnlyList<LinhaDoExtrato>> Linhas(FiltroDoExtrato filtro, CancellationToken cancelamento);
}
