using System.ComponentModel.DataAnnotations;

namespace Banco.Aplicacao.Documentos;

/// <summary>Os prazos da fila de extracao.</summary>
public sealed class OpcoesDoPipeline
{
    public const string Secao = "Pipeline";

    /// <summary>
    /// Quanto tempo a reserva de um documento vale.
    /// </summary>
    /// <remarks>
    /// Curto demais e o worker perde o documento no meio de uma extracao lenta, e dois
    /// processam o mesmo arquivo. Longo demais e o documento fica parado depois de o worker
    /// cair. A regra pratica e algumas vezes o pior tempo de extracao.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan PrazoDoLease { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Quanto o worker espera quando a fila esta vazia.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:05:00")]
    public TimeSpan IntervaloDeEspera { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Quantas reservas vencidas a varredura recupera por rodada.</summary>
    [Range(1, 500)]
    public int LotesDeRecuperacao { get; init; } = 20;

    /// <summary>
    /// Abaixo desta confianca o documento para para alguem olhar.
    /// </summary>
    /// <remarks>
    /// Configuravel porque e uma regua de operacao, e nao uma regra do negocio: quem opera aperta
    /// quando a extracao erra demais e afrouxa quando a fila de revisao cresce mais do que da para
    /// atender. O padrao de 0,80 deixa passar o boleto cuja linha digitavel fecha e cujo CNPJ
    /// ficou ambiguo, e barra tudo que dependa de valor achado no texto solto.
    /// </remarks>
    [Range(0.0, 1.0)]
    public decimal LimiarDeConfianca { get; init; } = 0.80m;

    /// <summary>Teto de documentos que a fila de revisao devolve por pagina.</summary>
    [Range(1, 200)]
    public int TamanhoMaximoDaRevisao { get; init; } = 50;
}
