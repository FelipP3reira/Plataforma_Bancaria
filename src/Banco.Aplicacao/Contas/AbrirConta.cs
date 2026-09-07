using Banco.Aplicacao.Portas;
using Banco.Dominio.Contas;

namespace Banco.Aplicacao.Contas;

public sealed record PedidoDeAbertura(string Titular);

public sealed record ContaAberta(Guid Id, string Numero, string Titular, decimal Saldo, DateTimeOffset AbertaEm);

public sealed class AbrirConta
{
    private readonly IRepositorioDeContas contas;
    private readonly IUnidadeDeTrabalho unidade;
    private readonly TimeProvider relogio;

    public AbrirConta(IRepositorioDeContas contas, IUnidadeDeTrabalho unidade, TimeProvider relogio)
    {
        this.contas = contas;
        this.unidade = unidade;
        this.relogio = relogio;
    }

    /// <remarks>
    /// Sem chave de idempotencia: abertura nao e operacao financeira, nao move dinheiro, e
    /// duas aberturas repetidas dao duas contas vazias — chato, e nao um prejuizo. Exigir
    /// chave aqui seria cerimonia sem risco por tras.
    /// </remarks>
    public async Task<ContaAberta> Executar(PedidoDeAbertura pedido, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);

        var numero = await contas.ProximoNumeroDeConta(cancelamento).ConfigureAwait(false);
        var conta = Conta.Abrir(numero, pedido.Titular, relogio.GetUtcNow());

        contas.Adicionar(conta);
        await unidade.Salvar(cancelamento).ConfigureAwait(false);

        return new ContaAberta(conta.Id, conta.Numero.Texto, conta.Titular, conta.Saldo.Valor, conta.AbertaEm);
    }
}
