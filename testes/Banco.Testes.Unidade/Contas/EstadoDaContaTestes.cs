using Banco.Dominio.Comum;
using Banco.Dominio.Contas;
using Banco.Dominio.Erros;
using Banco.Dominio.Ledger;

namespace Banco.Testes.Unidade.Contas;

public class EstadoDaContaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static Conta Aberta(decimal saldo = 0m)
    {
        var conta = Conta.Abrir(NumeroDaConta.DaSequencia(2001), "Ana Ribeiro", Agora);

        if (saldo > 0m)
        {
            conta.Creditar(Pedido(saldo));
        }

        return conta;
    }

    private static PedidoDeLancamento Pedido(decimal valor) =>
        new(Dinheiro.De(valor), "movimentacao", "api:teste", Guid.NewGuid().ToString(), Agora);

    [Fact]
    public void ContaNasceAtiva()
    {
        var conta = Aberta();

        Assert.Equal(EstadoDaConta.Ativa, conta.Estado);
        Assert.Equal(0, conta.SequenciaDeEstado);
    }

    /// <summary>
    /// O ponto do bloqueio: impede movimentacao nova sem apagar nada do que ja existe.
    /// </summary>
    [Fact]
    public void BloquearImpedeMovimentacaoEPreservaSaldoEHistorico()
    {
        var conta = Aberta(500m);

        conta.Bloquear("suspeita de fraude", "operador:felipe", Agora);

        Assert.Equal(EstadoDaConta.Bloqueada, conta.Estado);
        Assert.Equal(500m, conta.Saldo.Valor);
        Assert.Equal(1, conta.UltimaSequencia);

        Assert.Throws<ContaBloqueadaException>(() => conta.Sacar());
        Assert.Throws<ContaBloqueadaException>(() => conta.Depositar());
    }

    /// <summary>
    /// Credito tambem e barrado. Conta sob investigacao que ainda recebe dinheiro vira
    /// caixa de passagem justamente enquanto esta sendo investigada.
    /// </summary>
    [Fact]
    public void ContaBloqueadaNaoRecebeDinheiro()
    {
        var conta = Aberta(100m);
        conta.Bloquear("suspeita", "operador:felipe", Agora);

        var erro = Assert.Throws<ContaBloqueadaException>(() => conta.Creditar(Pedido(50m)));

        Assert.Contains("Bloqueada", erro.Message, StringComparison.Ordinal);
        Assert.Equal(100m, conta.Saldo.Valor);
    }

    // Conta bloqueada e sem dinheiro reclama do bloqueio, que e o problema de verdade.
    [Fact]
    public void ContaBloqueadaESemSaldoReclamaDoBloqueioENaoDoSaldo()
    {
        var conta = Aberta();
        conta.Bloquear("suspeita", "operador:felipe", Agora);

        Assert.Throws<ContaBloqueadaException>(() => conta.Debitar(Pedido(10m)));
    }

    [Fact]
    public void DesbloquearVoltaAMovimentar()
    {
        var conta = Aberta(200m);
        conta.Bloquear("suspeita", "operador:felipe", Agora);

        conta.Desbloquear("apurado, sem indicio", "operador:felipe", Agora.AddDays(1));

        Assert.Equal(EstadoDaConta.Ativa, conta.Estado);
        Assert.Equal(2, conta.SequenciaDeEstado);
        Assert.Equal(250m, conta.Creditar(Pedido(50m)).SaldoDepois.Valor);
    }

    [Fact]
    public void BloquearDuasVezesEhRecusado()
    {
        var conta = Aberta();
        conta.Bloquear("suspeita", "operador:felipe", Agora);

        Assert.Throws<TransicaoInvalidaException>(
            () => conta.Bloquear("de novo", "operador:felipe", Agora));
    }

    [Fact]
    public void DesbloquearContaAtivaEhRecusado() =>
        Assert.Throws<TransicaoInvalidaException>(
            () => Aberta().Desbloquear("nada a desbloquear", "operador:felipe", Agora));

    /// <summary>
    /// Encerrar com saldo deixaria dinheiro sem dono numa conta que ninguem movimenta.
    /// </summary>
    [Fact]
    public void EncerrarComSaldoEhRecusado()
    {
        var conta = Aberta(0.01m);

        var erro = Assert.Throws<TransicaoInvalidaException>(
            () => conta.Encerrar("cliente pediu", "operador:felipe", Agora));

        Assert.Contains("saldo", erro.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EstadoDaConta.Ativa, conta.Estado);
    }

    [Fact]
    public void EncerrarZeradaFunciona()
    {
        var conta = Aberta(100m);
        conta.Debitar(Pedido(100m));

        conta.Encerrar("cliente pediu", "operador:felipe", Agora);

        Assert.Equal(EstadoDaConta.Encerrada, conta.Estado);
    }

    [Fact]
    public void ContaBloqueadaPodeSerEncerradaSeEstiverZerada()
    {
        var conta = Aberta();
        conta.Bloquear("suspeita", "operador:felipe", Agora);

        conta.Encerrar("encerramento compulsorio", "operador:felipe", Agora);

        Assert.Equal(EstadoDaConta.Encerrada, conta.Estado);
    }

    // Encerrada e terminal: reabrir apagaria o motivo do encerramento.
    [Theory]
    [InlineData(EstadoDaConta.Ativa)]
    [InlineData(EstadoDaConta.Bloqueada)]
    [InlineData(EstadoDaConta.Encerrada)]
    public void NadaSaiDeEncerrada(EstadoDaConta destino) =>
        Assert.False(MaquinaDeEstadosDaConta.Aceita(EstadoDaConta.Encerrada, destino));

    [Fact]
    public void ContaEncerradaNaoMovimenta()
    {
        var conta = Aberta();
        conta.Encerrar("cliente pediu", "operador:felipe", Agora);

        Assert.Throws<ContaBloqueadaException>(() => conta.Creditar(Pedido(10m)));
        Assert.Throws<ContaBloqueadaException>(() => conta.Debitar(Pedido(10m)));
    }

    /// <summary>
    /// A tabela inteira contra uma copia escrita a parte, a partir do desenho do fluxo.
    /// Duplicar o dado e o objetivo: divergencia entre as duas versoes acusa erro de
    /// digitacao na tabela real.
    /// </summary>
    [Fact]
    public void ATabelaDeTransicoesBateComOFluxoDesenhado()
    {
        var esperadas = new HashSet<(EstadoDaConta, EstadoDaConta)>
        {
            (EstadoDaConta.Ativa, EstadoDaConta.Bloqueada),
            (EstadoDaConta.Ativa, EstadoDaConta.Encerrada),
            (EstadoDaConta.Bloqueada, EstadoDaConta.Ativa),
            (EstadoDaConta.Bloqueada, EstadoDaConta.Encerrada),
        };

        foreach (var de in Enum.GetValues<EstadoDaConta>())
        {
            foreach (var para in Enum.GetValues<EstadoDaConta>())
            {
                Assert.Equal(esperadas.Contains((de, para)), MaquinaDeEstadosDaConta.Aceita(de, para));
            }
        }
    }

    [Fact]
    public void AMudancaGuardaMotivoOrigemESequencia()
    {
        var conta = Aberta();

        var mudanca = conta.Bloquear("suspeita de fraude", "operador:felipe", Agora);

        Assert.Equal(conta.Id, mudanca.ContaId);
        Assert.Equal(1, mudanca.Sequencia);
        Assert.Equal(EstadoDaConta.Ativa, mudanca.De);
        Assert.Equal(EstadoDaConta.Bloqueada, mudanca.Para);
        Assert.Equal("suspeita de fraude", mudanca.Motivo);
        Assert.Equal("operador:felipe", mudanca.Origem);
        Assert.Equal(Agora, mudanca.OcorridaEm);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MudancaSemMotivoEhRecusada(string motivo) =>
        Assert.Throws<ContaInvalidaException>(() => Aberta().Bloquear(motivo, "operador:felipe", Agora));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MudancaSemOrigemEhRecusada(string origem) =>
        Assert.Throws<ContaInvalidaException>(() => Aberta().Bloquear("suspeita", origem, Agora));

    [Fact]
    public void MotivoLongoDemaisEhRecusado() =>
        Assert.Throws<ContaInvalidaException>(
            () => Aberta().Bloquear(
                new string('m', MudancaDeEstadoDaConta.TamanhoMaximoDoMotivo + 1),
                "operador:felipe",
                Agora));
}

internal static class AtalhosDaConta
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    public static Lancamento Sacar(this Conta conta) =>
        conta.Debitar(new PedidoDeLancamento(
            Dinheiro.De(1m), "saque", "api:teste", Guid.NewGuid().ToString(), Agora));

    public static Lancamento Depositar(this Conta conta) =>
        conta.Creditar(new PedidoDeLancamento(
            Dinheiro.De(1m), "deposito", "api:teste", Guid.NewGuid().ToString(), Agora));
}
