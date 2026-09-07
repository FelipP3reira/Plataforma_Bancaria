using System.Globalization;
using System.Net;

namespace Banco.Testes.Integracao;

[Collection(ColecaoDaApi.Nome)]
public class ExtratoTestes
{
    private readonly FabricaDaApi fabrica;

    public ExtratoTestes(FabricaDaApi fabrica) => this.fabrica = fabrica;

    private static string Instante(DateTimeOffset quando) =>
        Uri.EscapeDataString(quando.ToString("O", CultureInfo.InvariantCulture));

    [Fact]
    public async Task OExtratoVemDoMaisRecenteParaOMaisAntigo()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(1000m);
        await cliente.Sacar(conta, 100m, descricao: "saque um");
        await cliente.Depositar(conta, 50m, descricao: "deposito dois");

        var extrato = await cliente.Extrato(conta);

        Assert.Equal([3L, 2L, 1L], extrato.Linhas.Select(linha => linha.Sequencia));
        Assert.Equal("deposito dois", extrato.Linhas[0].Descricao);
        Assert.Null(extrato.ProximaPagina);
    }

    /// <summary>
    /// O saldo corrente vem gravado em cada linha, e nao de uma soma feita no cliente:
    /// o topo do extrato tem que bater com o saldo da conta.
    /// </summary>
    [Fact]
    public async Task OSaldoDeCadaLinhaContaAHistoriaAteEla()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(1000m);
        await cliente.Sacar(conta, 250.50m);

        var extrato = await cliente.Extrato(conta);

        Assert.Equal([749.50m, 1000m], extrato.Linhas.Select(linha => linha.SaldoDepois));
        Assert.Equal(749.50m, (await cliente.Detalhe(conta))!.Saldo);
    }

    // Debito com sinal negativo: quem soma uma coluna de efeitos chega no saldo.
    [Fact]
    public async Task OEfeitoDoDebitoVemNegativoEOSomatorioDaOSaldo()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(300m);
        await cliente.Sacar(conta, 120m);

        var extrato = await cliente.Extrato(conta);

        Assert.Equal([-120m, 300m], extrato.Linhas.Select(linha => linha.Efeito));
        Assert.Equal(180m, extrato.Linhas.Sum(linha => linha.Efeito));
    }

    /// <summary>
    /// Paginar tem que percorrer a lista inteira uma vez: nem repetindo linha, nem pulando.
    /// </summary>
    [Fact]
    public async Task APaginacaoPercorreTudoSemRepetirNemPular()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(1000m);

        for (var i = 0; i < 6; i++)
        {
            (await cliente.Sacar(conta, 10m, descricao: $"saque {i}")).EnsureSuccessStatusCode();
        }

        var vistas = new List<long>();
        string? marcador = null;
        var paginas = 0;

        do
        {
            var pagina = await cliente.Extrato(
                conta,
                marcador is null ? "?tamanho=3" : $"?tamanho=3&pagina={Uri.EscapeDataString(marcador)}");

            vistas.AddRange(pagina.Linhas.Select(linha => linha.Sequencia));
            marcador = pagina.ProximaPagina;
            paginas++;
        }

        // Trava de seguranca: marcador que nao anda faz o cliente pedir a mesma pagina para
        // sempre. Sem o limite, a falha seria um teste travado em vez de um teste vermelho.
        while (marcador is not null && paginas < 10);

        Assert.Equal(3, paginas);
        Assert.Equal([7L, 6L, 5L, 4L, 3L, 2L, 1L], vistas);
    }

    /// <summary>
    /// A ultima pagina cheia nao pode anunciar uma proxima que nao existe — o cliente
    /// pediria uma pagina vazia so para descobrir que acabou.
    /// </summary>
    [Fact]
    public async Task PaginaExataNaoAnunciaProximaPagina()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(100m);
        await cliente.Sacar(conta, 10m);

        var extrato = await cliente.Extrato(conta, "?tamanho=2");

        Assert.Equal(2, extrato.Linhas.Count);
        Assert.Null(extrato.ProximaPagina);
    }

    [Fact]
    public async Task OFiltroDePeriodoCortaOQueEstaFora()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(500m);

        // O relogio da API e controlado: sem avancar, tudo cairia no mesmo instante e o
        // corte por periodo nao teria o que separar.
        try
        {
            fabrica.Relogio.Avancar(TimeSpan.FromDays(1));
            var virada = fabrica.Relogio.GetUtcNow();
            (await cliente.Sacar(conta, 30m, descricao: "depois da virada")).EnsureSuccessStatusCode();

            var antigos = await cliente.Extrato(conta, $"?ate={Instante(virada.AddSeconds(-1))}");
            var recentes = await cliente.Extrato(conta, $"?de={Instante(virada)}");

            Assert.Equal(["deposito"], antigos.Linhas.Select(linha => linha.Descricao));
            Assert.Equal(["depois da virada"], recentes.Linhas.Select(linha => linha.Descricao));
        }
        finally
        {
            fabrica.Relogio.Reiniciar();
        }
    }

    [Fact]
    public async Task PeriodoSemLancamentoDevolveListaVazia()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(100m);
        var futuro = fabrica.Relogio.GetUtcNow().AddDays(30);

        var extrato = await cliente.Extrato(
            conta,
            $"?de={Instante(futuro)}&ate={Instante(futuro.AddDays(1))}");

        Assert.Empty(extrato.Linhas);
        Assert.Null(extrato.ProximaPagina);
    }

    /// <summary>
    /// O ponto do bloqueio, do lado da consulta: barra movimentacao e mantem o extrato.
    /// Cortar o extrato tiraria a informacao de quem precisa apurar a suspeita.
    /// </summary>
    [Fact]
    public async Task ContaBloqueadaEEncerradaContinuamComExtrato()
    {
        var cliente = fabrica.CreateClient();
        var bloqueada = await cliente.ContaCom(200m);
        await cliente.Bloquear(bloqueada, "suspeita");

        var encerrada = await cliente.ContaCom(80m);
        await cliente.Sacar(encerrada, 80m);
        await cliente.Encerrar(encerrada, "cliente pediu");

        Assert.Single((await cliente.Extrato(bloqueada)).Linhas);
        Assert.Equal(2, (await cliente.Extrato(encerrada)).Linhas.Count);
    }

    // A perna da transferencia sai identificada, para o cliente ligar as duas pontas.
    [Fact]
    public async Task ALinhaDeTransferenciaApontaParaATransferencia()
    {
        var cliente = fabrica.CreateClient();
        var origem = await cliente.ContaCom(300m);
        var destino = await cliente.ContaCom(0m);

        var transferencia = await (await cliente.Transferir(origem, destino, 75m)).Transferencia();

        var saida = (await cliente.Extrato(origem)).Linhas[0];
        var entrada = (await cliente.Extrato(destino)).Linhas[0];

        Assert.Equal(transferencia!.Id, saida.TransferenciaId);
        Assert.Equal(transferencia.Id, entrada.TransferenciaId);
        Assert.Equal(-75m, saida.Efeito);
        Assert.Equal(75m, entrada.Efeito);
    }

    [Fact]
    public async Task LancamentoAvulsoNaoApontaParaTransferenciaNenhuma()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(10m);

        Assert.Null((await cliente.Extrato(conta)).Linhas[0].TransferenciaId);
    }

    [Fact]
    public async Task ExtratoDeContaInexistenteDevolveNaoEncontrada() =>
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await fabrica.CreateClient().ExtratoBruto(Guid.NewGuid())).StatusCode);

    [Theory]
    [InlineData("?tamanho=0")]
    [InlineData("?tamanho=201")]
    public async Task TamanhoForaDaFaixaEhRecusado(string consulta)
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(10m);

        var resposta = await cliente.ExtratoBruto(conta, consulta);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("Pedido invalido", await resposta.TituloDoProblema());
    }

    [Fact]
    public async Task PeriodoInvertidoEhRecusado()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(10m);
        var agora = fabrica.Relogio.GetUtcNow();

        var resposta = await cliente.ExtratoBruto(
            conta,
            $"?de={Instante(agora)}&ate={Instante(agora.AddDays(-1))}");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    // Marcador digitado a mao e erro do pedido, e nao falha do servidor.
    [Fact]
    public async Task MarcadorMalFormadoDevolveErroDoPedido()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(10m);

        var resposta = await cliente.ExtratoBruto(conta, "?pagina=isso-nao-e-marcador");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }
}
