using Banco.Aplicacao.Erros;

namespace Banco.Aplicacao.Extrato;

/// <summary>
/// O periodo pedido, ja resolvido: sem campo opcional e sem valor a combinar depois.
/// </summary>
/// <remarks>
/// As duas pontas sao inclusivas. Um extrato pedido "de 01/09 a 30/09" que escondesse o
/// lancamento das 00:00:00 do dia 30 seria um extrato errado, e ninguem descobriria olhando
/// a tela.
/// </remarks>
public sealed record FiltroDoExtrato(
    Guid ContaId,
    DateTimeOffset De,
    DateTimeOffset Ate,
    int Tamanho,
    MarcadorDoExtrato? Marcador)
{
    public const int TamanhoPadrao = 50;
    public const int TamanhoMaximo = 200;

    public void Validar()
    {
        if (De > Ate)
        {
            throw new ConsultaInvalidaException("O inicio do periodo e depois do fim.");
        }

        // Recusa em vez de aparar para o maximo: cliente que pede 5000 e recebe 200 sem
        // aviso conclui que a conta so tem 200 lancamentos no periodo.
        if (Tamanho is < 1 or > TamanhoMaximo)
        {
            throw new ConsultaInvalidaException(
                $"O tamanho da pagina precisa estar entre 1 e {TamanhoMaximo}.");
        }

        // O marcador so faz sentido dentro do periodo que ele mesmo paginou. Fora dele, a
        // paginacao andaria em cima de um recorte diferente do que gerou o marcador.
        if (Marcador is { } marcador && (marcador.CriadoEm < De || marcador.CriadoEm > Ate))
        {
            throw new ConsultaInvalidaException("O marcador nao pertence ao periodo pedido.");
        }
    }
}
