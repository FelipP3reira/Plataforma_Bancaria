using Banco.Dominio.Comum;
using Banco.Dominio.Erros;

namespace Banco.Dominio.Ledger;

/// <summary>
/// Tudo que uma movimentacao precisa carregar para virar linha de ledger.
/// </summary>
/// <remarks>
/// Um parametro so, e nao cinco soltos, porque os cinco andam sempre juntos: nenhum
/// lancamento deste sistema pode existir sem valor, sem quem pediu, sem chave de
/// idempotencia e sem o instante. Deixa-los na assinatura como opcionais convidaria a
/// esquecer justamente os dois que so fazem falta na auditoria, meses depois.
/// </remarks>
/// <param name="ChaveIdempotencia">
/// Vem do cliente. Toda operacao financeira exige a dela: e o que faz um reenvio por tempo
/// esgotado ser reconhecido como o mesmo deposito, e nao como um segundo.
/// </param>
/// <param name="Origem">Quem mandou fazer, para a auditoria.</param>
public sealed record PedidoDeLancamento(
    Dinheiro Valor,
    string Descricao,
    string Origem,
    string ChaveIdempotencia,
    DateTimeOffset Agora)
{
    public const int TamanhoMaximoDaChave = 64;
    public const int TamanhoMaximoDaOrigem = 100;

    internal void Validar()
    {
        if (Valor.EhZero)
        {
            throw new ValorInvalidoException("Movimentacao de valor zero nao muda nada e nao vira lancamento.");
        }

        if (string.IsNullOrWhiteSpace(ChaveIdempotencia) || ChaveIdempotencia.Length > TamanhoMaximoDaChave)
        {
            throw new ContaInvalidaException(
                $"Chave de idempotencia obrigatoria e de ate {TamanhoMaximoDaChave} caracteres.");
        }

        if (string.IsNullOrWhiteSpace(Origem) || Origem.Trim().Length > TamanhoMaximoDaOrigem)
        {
            throw new ContaInvalidaException(
                $"Origem obrigatoria e de ate {TamanhoMaximoDaOrigem} caracteres.");
        }

        if (string.IsNullOrWhiteSpace(Descricao)
            || Descricao.Trim().Length > Lancamento.TamanhoMaximoDaDescricao)
        {
            throw new ContaInvalidaException(
                $"Descricao obrigatoria e de ate {Lancamento.TamanhoMaximoDaDescricao} caracteres.");
        }
    }
}
