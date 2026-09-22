using Banco.Aplicacao.Documentos;

namespace Banco.Worker;

/// <summary>
/// O laco que consome a fila de documentos.
/// </summary>
/// <remarks>
/// Consulta periodica, e nao fila dedicada. O banco ja esta aqui, ja e transacional e a
/// trava que serializa dois workers e a mesma que serializa duas transferencias — trazer
/// RabbitMQ ou SQS para esta escala adicionaria um componente para operar, um ponto de falha
/// e a duvida de sempre: o que acontece quando a mensagem sai da fila e a transacao no banco
/// e desfeita. Com a fila dentro do banco, essa duvida nao existe.
/// <para>
/// Passa a valer a pena quando a espera media entre chegar e ser processado incomodar, ou
/// quando os workers estiverem espalhados em maquinas o bastante para a consulta periodica
/// pesar no banco. Nenhum dos dois e o caso aqui.
/// </para>
/// </remarks>
internal sealed partial class ServicoDeExtracao : BackgroundService
{
    private readonly IServiceScopeFactory escopos;
    private readonly OpcoesDoPipeline opcoes;
    private readonly ILogger<ServicoDeExtracao> registro;

    public ServicoDeExtracao(
        IServiceScopeFactory escopos,
        OpcoesDoPipeline opcoes,
        ILogger<ServicoDeExtracao> registro)
    {
        this.escopos = escopos;
        this.opcoes = opcoes;
        this.registro = registro;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RegistrarSubida(opcoes.IntervaloDeEspera, opcoes.PrazoDoLease);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await UmaRodada(stoppingToken).ConfigureAwait(false))
                {
                    // Achou trabalho: volta na hora, sem esperar. A espera existe para nao
                    // martelar o banco com a fila vazia, e nao para desacelerar o processo
                    // justamente quando ha fila.
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception erro)
            {
                // O laco nao morre por causa de uma rodada. Banco fora do ar e a falha que
                // mais aparece aqui, e ela volta sozinha — worker que encerra no primeiro
                // erro precisaria de alguem para religar.
                RegistrarErroDaRodada(erro);
            }

            await Task.Delay(opcoes.IntervaloDeEspera, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <returns>Verdadeiro se havia documento na fila.</returns>
    private async Task<bool> UmaRodada(CancellationToken cancelamento)
    {
        // Escopo por rodada, e nao por processo: o DbContext acumula as entidades que
        // rastreia, e um unico escopo vivo por dias faria o worker crescer em memoria a cada
        // documento processado.
        await using var escopo = escopos.CreateAsyncScope();
        var pipeline = escopo.ServiceProvider.GetRequiredService<ExtrairProximoDocumento>();

        // A recuperacao vem antes da fila de proposito: documento preso por worker derrubado
        // volta a ser elegivel agora, e nao depois de a fila normal esvaziar.
        await pipeline.RecuperarPresos(cancelamento).ConfigureAwait(false);

        return await pipeline.Executar(cancelamento).ConfigureAwait(false);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Worker de extracao no ar: espera de {Intervalo} e reserva de {Prazo}")]
    private partial void RegistrarSubida(TimeSpan intervalo, TimeSpan prazo);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rodada de extracao falhou; o laco continua")]
    private partial void RegistrarErroDaRodada(Exception erro);
}
