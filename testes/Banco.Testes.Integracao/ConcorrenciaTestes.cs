using System.Net;

namespace Banco.Testes.Integracao;

/// <summary>
/// A prova de que a trava pessimista faz o que se espera dela.
/// </summary>
/// <remarks>
/// Estes testes disparam requisicoes de verdade, ao mesmo tempo, contra um SQL Server de
/// verdade. Nao ha como provar isso com dublê: o que esta sendo testado e o comportamento
/// do banco sob <c>UPDLOCK</c> dentro de uma transacao, e nao o codigo em volta.
/// </remarks>
[Collection(ColecaoDaApi.Nome)]
public class ConcorrenciaTestes
{
    private readonly FabricaDaApi fabrica;

    public ConcorrenciaTestes(FabricaDaApi fabrica) => this.fabrica = fabrica;

    /// <summary>
    /// Dois saques simultaneos, cada um do saldo inteiro. Sem trava, os dois leem cem, os
    /// dois concluem que da, e a conta termina devendo cem reais que nunca existiram.
    /// </summary>
    /// <remarks>
    /// O motivo da recusa faz parte do teste, e nao so o codigo de status. Sem a trava os
    /// dois saques passam pela conferencia de saldo e colidem no indice unico do ledger —
    /// o que tambem devolve 409, mas dizendo "conflito de gravacao". Verificar so o status
    /// deixaria este teste passar com a trava removida, provando o indice em vez da trava.
    /// A resposta certa e "saldo insuficiente": a segunda requisicao so decidiu depois que
    /// a primeira terminou, e ai realmente nao havia mais dinheiro.
    /// </remarks>
    [Fact]
    public async Task DoisSaquesDoSaldoInteiroAoMesmoTempoSoDeixamUmPassar()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(100m);

        var partida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // As duas so saem depois que as duas estao prontas: montar a requisicao leva mais
        // tempo do que a corrida dura, e sem o portao a primeira terminaria antes de a
        // segunda comecar.
        var saques = Enumerable.Range(0, 2).Select(async _ =>
        {
            await partida.Task;
            return await cliente.Sacar(conta, 100m);
        }).ToArray();

        partida.SetResult();
        var respostas = await Task.WhenAll(saques);

        Assert.Equal(1, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Created));

        var recusada = Assert.Single(respostas, resposta => resposta.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("Saldo insuficiente", await recusada.TituloDoProblema());

        var detalhe = await cliente.Detalhe(conta);
        Assert.Equal(0m, detalhe!.Saldo);
        Assert.Equal(2, detalhe.Lancamentos);
    }

    /// <summary>
    /// Dez saques simultaneos de dez reais numa conta com cinquenta: exatamente cinco
    /// passam. Prova que a trava serializa em vez de so reduzir a chance de colidir.
    /// </summary>
    [Fact]
    public async Task DezSaquesSimultaneosParamExatamenteNoSaldo()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.ContaCom(50m);

        var partida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saques = Enumerable.Range(0, 10).Select(async _ =>
        {
            await partida.Task;
            return await cliente.Sacar(conta, 10m);
        }).ToArray();

        partida.SetResult();
        var respostas = await Task.WhenAll(saques);

        Assert.Equal(5, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Created));

        var recusadas = respostas.Where(resposta => resposta.StatusCode == HttpStatusCode.Conflict).ToList();
        Assert.Equal(5, recusadas.Count);

        // Todas as cinco recusas tem que ser por saldo, e nenhuma por colisao de gravacao.
        foreach (var recusada in recusadas)
        {
            Assert.Equal("Saldo insuficiente", await recusada.TituloDoProblema());
        }

        var detalhe = await cliente.Detalhe(conta);
        Assert.Equal(0m, detalhe!.Saldo);
        Assert.Equal(6, detalhe.Lancamentos);
    }

    /// <summary>
    /// Vinte depositos ao mesmo tempo: nenhum pode se perder, e a sequencia do ledger nao
    /// pode ter buraco nem repeticao.
    /// </summary>
    /// <remarks>
    /// Credito nao tem regra de saldo, entao aqui nao ha nada para recusar — o que se testa
    /// e que as vinte gravacoes concorrentes produzem vinte posicoes distintas e seguidas.
    /// </remarks>
    [Fact]
    public async Task VinteDepositosSimultaneosNaoPerdemNenhumCentavoNemPosicao()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();

        var partida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var depositos = Enumerable.Range(0, 20).Select(async _ =>
        {
            await partida.Task;
            return await cliente.Depositar(conta.Id, 7.13m);
        }).ToArray();

        partida.SetResult();
        var respostas = await Task.WhenAll(depositos);

        Assert.All(respostas, resposta => Assert.Equal(HttpStatusCode.Created, resposta.StatusCode));

        var sequencias = new List<long>();
        foreach (var resposta in respostas)
        {
            sequencias.Add((await resposta.Lancamento())!.Sequencia);
        }

        Assert.Equal(Enumerable.Range(1, 20).Select(numero => (long)numero), sequencias.Order());
        Assert.Equal(20 * 7.13m, (await cliente.Detalhe(conta.Id))!.Saldo);
    }

    /// <summary>
    /// O mesmo reenvio disparado varias vezes ao mesmo tempo. E o caso real do cliente
    /// nervoso clicando de novo enquanto a primeira requisicao ainda esta no ar.
    /// </summary>
    /// <remarks>
    /// A conferencia de idempotencia roda depois de pegar a trava, e e isso que faz este
    /// teste passar: se ela rodasse antes, as cinco consultariam juntas, as cinco se
    /// achariam a primeira, e mais de uma depositaria.
    /// </remarks>
    [Fact]
    public async Task CincoReenviosDaMesmaChaveAoMesmoTempoDepositamUmaVezSo()
    {
        var cliente = fabrica.CreateClient();
        var conta = await cliente.AbrirConta();
        var chave = Pedidos.ChaveNova();

        var partida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var envios = Enumerable.Range(0, 5).Select(async _ =>
        {
            await partida.Task;
            return await cliente.Depositar(conta.Id, 250m, chave);
        }).ToArray();

        partida.SetResult();
        var respostas = await Task.WhenAll(envios);

        Assert.All(respostas, resposta => Assert.True(resposta.IsSuccessStatusCode, $"{resposta.StatusCode}"));
        Assert.Equal(1, respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.Created));

        var detalhe = await cliente.Detalhe(conta.Id);
        Assert.Equal(250m, detalhe!.Saldo);
        Assert.Equal(1, detalhe.Lancamentos);
    }

    // Contas diferentes nao disputam nada: a trava e por linha, e nao pela tabela.
    [Fact]
    public async Task SaquesEmContasDiferentesNaoDisputamEntreSi()
    {
        var cliente = fabrica.CreateClient();
        var contas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => cliente.ContaCom(100m)));

        var partida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saques = contas.Select(async conta =>
        {
            await partida.Task;
            return await cliente.Sacar(conta, 100m);
        }).ToArray();

        partida.SetResult();
        var respostas = await Task.WhenAll(saques);

        Assert.All(respostas, resposta => Assert.Equal(HttpStatusCode.Created, resposta.StatusCode));
    }
}
