using Banco.Aplicacao.Erros;
using Banco.Aplicacao.Portas;
using Banco.Dominio.Comum;
using Banco.Dominio.Contas;
using Banco.Dominio.Erros;
using Banco.Dominio.Ledger;
using Banco.Dominio.Transferencias;

namespace Banco.Aplicacao.Transferencias;

public sealed record PedidoDeTransferencia(
    Guid ContaOrigemId,
    Guid ContaDestinoId,
    decimal Valor,
    string Descricao,
    string Origem,
    string ChaveIdempotencia);

public sealed record ResultadoDaTransferencia(Transferencia Transferencia, bool Nova);

public sealed class TransferirEntreContas
{
    private readonly IRepositorioDeContas contas;
    private readonly IRepositorioDeTransferencias transferencias;
    private readonly IUnidadeDeTrabalho unidade;
    private readonly TimeProvider relogio;

    public TransferirEntreContas(
        IRepositorioDeContas contas,
        IRepositorioDeTransferencias transferencias,
        IUnidadeDeTrabalho unidade,
        TimeProvider relogio)
    {
        this.contas = contas;
        this.transferencias = transferencias;
        this.unidade = unidade;
        this.relogio = relogio;
    }

    public async Task<ResultadoDaTransferencia> Executar(
        PedidoDeTransferencia pedido,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);

        if (pedido.ContaOrigemId == pedido.ContaDestinoId)
        {
            throw new ContaInvalidaException("Origem e destino sao a mesma conta.");
        }

        await using var transacao = await unidade.Abrir(cancelamento).ConfigureAwait(false);

        var (origem, destino) = await TravarNaOrdem(
            pedido.ContaOrigemId,
            pedido.ContaDestinoId,
            cancelamento).ConfigureAwait(false);

        // Depois da trava, como no deposito e no saque: antes dela, dois envios com a mesma
        // chave consultariam juntos, os dois se achariam o primeiro, e o indice unico
        // recusaria um deles com a outra transferencia ja feita.
        if (await transferencias
                .PorChaveDeIdempotencia(origem.Id, pedido.ChaveIdempotencia, cancelamento)
                .ConfigureAwait(false) is { } existente)
        {
            GarantirQueEhOMesmoPedido(existente, pedido);

            await transacao.Confirmar(cancelamento).ConfigureAwait(false);
            return new ResultadoDaTransferencia(existente, Nova: false);
        }

        var realizada = Transferencia.Entre(
            origem,
            destino,
            new PedidoDeLancamento(
                Dinheiro.De(pedido.Valor),
                pedido.Descricao,
                pedido.Origem,
                pedido.ChaveIdempotencia,
                relogio.GetUtcNow()));

        transferencias.Adicionar(realizada.Transferencia);
        contas.Adicionar(realizada.Debito);
        contas.Adicionar(realizada.Credito);

        // Um Salvar so, dentro de uma transacao so: as duas pernas e o registro entram
        // juntos ou nao entra nada. E isso que faz a transferencia ser atomica.
        await unidade.Salvar(cancelamento).ConfigureAwait(false);
        await transacao.Confirmar(cancelamento).ConfigureAwait(false);

        return new ResultadoDaTransferencia(realizada.Transferencia, Nova: true);
    }

    /// <summary>
    /// Trava as duas contas sempre na mesma ordem, ditada pelo id.
    /// </summary>
    /// <remarks>
    /// Esta e a diferenca entre a trava pessimista funcionar e o sistema travar sozinho.
    /// Uma transferencia de A para B trava A e depois B; uma de B para A, ao mesmo tempo,
    /// travaria B e depois A — e cada uma ficaria esperando a trava que a outra ja tem.
    /// O SQL Server detecta isso e mata uma das duas como vitima de deadlock, o que vira
    /// erro de servidor numa operacao que estava perfeitamente correta.
    /// <para>
    /// Ordenar pelo id resolve porque todo mundo pega as travas na mesma sequencia: quem
    /// chegar depois espera na primeira, e nao no meio. A ordem em si nao precisa
    /// significar nada — precisa so ser a mesma em todo processo, e a comparacao de
    /// <see cref="Guid"/> no .NET e determinista e igual em qualquer maquina.
    /// </para>
    /// </remarks>
    private async Task<(Conta Origem, Conta Destino)> TravarNaOrdem(
        Guid origemId,
        Guid destinoId,
        CancellationToken cancelamento)
    {
        var origemPrimeiro = origemId.CompareTo(destinoId) < 0;
        var primeiroId = origemPrimeiro ? origemId : destinoId;
        var segundoId = origemPrimeiro ? destinoId : origemId;

        var primeira = await Travar(primeiroId, cancelamento).ConfigureAwait(false);
        var segunda = await Travar(segundoId, cancelamento).ConfigureAwait(false);

        return origemPrimeiro ? (primeira, segunda) : (segunda, primeira);
    }

    private async Task<Conta> Travar(Guid id, CancellationToken cancelamento) =>
        await contas.PorIdParaMovimentar(id, cancelamento).ConfigureAwait(false)
        ?? throw new ContaNaoEncontradaException($"Conta {id} nao encontrada.");

    private static void GarantirQueEhOMesmoPedido(Transferencia existente, PedidoDeTransferencia pedido)
    {
        if (existente.ContaDestinoId != pedido.ContaDestinoId || existente.Valor.Valor != pedido.Valor)
        {
            throw new ChaveDeIdempotenciaReutilizadaException(
                $"A chave '{pedido.ChaveIdempotencia}' ja foi usada nesta conta para outra transferencia.");
        }
    }
}
