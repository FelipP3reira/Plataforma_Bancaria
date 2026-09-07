using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Portas;
using Banco.Dominio.Contas;

namespace Banco.Aplicacao.Contas;

public sealed record DetalheDaConta(
    Guid Id,
    string Numero,
    string Titular,
    EstadoDaConta Estado,
    decimal Saldo,
    long Lancamentos,
    DateTimeOffset AbertaEm,
    DateTimeOffset AtualizadaEm);

public sealed class ConsultarConta
{
    private readonly IRepositorioDeContas contas;

    public ConsultarConta(IRepositorioDeContas contas) => this.contas = contas;

    public async Task<DetalheDaConta> Executar(Guid id, CancellationToken cancelamento)
    {
        var conta = await contas.PorId(id, cancelamento).ConfigureAwait(false)
            ?? throw new ContaNaoEncontradaException($"Conta {id} nao encontrada.");

        // A quantidade de lancamentos sai de UltimaSequencia, e nao de um COUNT: a
        // sequencia nao pula, entao o numero ja esta ali. Contar seria uma varredura da
        // conta inteira para saber algo que a propria linha da conta ja diz.
        return new DetalheDaConta(
            conta.Id,
            conta.Numero.Texto,
            conta.Titular,
            conta.Estado,
            conta.Saldo.Valor,
            conta.UltimaSequencia,
            conta.AbertaEm,
            conta.AtualizadaEm);
    }
}
