using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Portas;

namespace Banco.Aplicacao.Extrato;

/// <param name="Marcador">Marcador devolvido pela pagina anterior, se houver.</param>
public sealed record PedidoDeExtrato(
    Guid ContaId,
    DateTimeOffset? De,
    DateTimeOffset? Ate,
    int? Tamanho,
    string? Marcador);

/// <summary>
/// O extrato paginado da conta.
/// </summary>
/// <remarks>
/// Vale para conta bloqueada e para conta encerrada. Bloqueio impede movimentar, e nao
/// consultar: cortar o extrato de quem esta sob suspeita tiraria a informacao justamente de
/// quem precisa apurar.
/// </remarks>
public sealed class ConsultarExtrato
{
    private readonly IRepositorioDeContas contas;
    private readonly IExtratoDaConta extrato;
    private readonly TimeProvider relogio;

    public ConsultarExtrato(IRepositorioDeContas contas, IExtratoDaConta extrato, TimeProvider relogio)
    {
        this.contas = contas;
        this.extrato = extrato;
        this.relogio = relogio;
    }

    public async Task<PaginaDoExtrato> Executar(PedidoDeExtrato pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);

        var conta = await contas.PorId(pedido.ContaId, cancelamento).ConfigureAwait(false)
            ?? throw new ContaNaoEncontradaException($"Conta {pedido.ContaId} nao encontrada.");

        var filtro = new FiltroDoExtrato(
            pedido.ContaId,
            pedido.De ?? conta.AbertaEm,
            pedido.Ate ?? relogio.GetUtcNow(),
            pedido.Tamanho ?? FiltroDoExtrato.TamanhoPadrao,
            pedido.Marcador is null ? null : MarcadorDoExtrato.Decodificar(pedido.Marcador));

        filtro.Validar();

        var linhas = await extrato.Linhas(filtro, cancelamento).ConfigureAwait(false);

        // A linha extra so serve para responder "tem mais?"; ela e a primeira da proxima
        // pagina, e nao desta.
        var temMais = linhas.Count > filtro.Tamanho;
        var pagina = temMais ? linhas.Take(filtro.Tamanho).ToArray() : linhas;

        return new PaginaDoExtrato(
            conta.Id,
            conta.Numero.Texto,
            filtro.De,
            filtro.Ate,
            pagina,
            temMais ? Marcador(pagina[^1]) : null);
    }

    private static string Marcador(LinhaDoExtrato ultima) =>
        new MarcadorDoExtrato(ultima.CriadoEm, ultima.Sequencia).Codificar();
}
