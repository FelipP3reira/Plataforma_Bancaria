namespace Banco.Dominio.Contas;

/// <summary>
/// Uma linha por mudanca de estado da conta, gravada na mesma transacao da mudanca.
/// </summary>
/// <remarks>
/// Registro imutavel, como o ledger: nao existe caminho para alterar nem apagar. Bloqueio
/// por suspeita e um evento que alguem vai ter que explicar depois — guardar so o estado
/// atual responderia "esta bloqueada" sem responder desde quando, por que, nem a mando de
/// quem.
/// </remarks>
/// <param name="Sequencia">
/// Posicao na trilha, comecando em 1. Nao da para ordenar so pela data: bloquear e
/// desbloquear na mesma requisicao gravariam o mesmo instante, e a trilha sairia em ordem
/// indefinida justamente onde ela precisa ser lida como sequencia.
/// </param>
public sealed class MudancaDeEstadoDaConta
{
    public const int TamanhoMaximoDoMotivo = 200;

    private MudancaDeEstadoDaConta()
    {
    }

    internal MudancaDeEstadoDaConta(
        Guid contaId,
        long sequencia,
        EstadoDaConta de,
        EstadoDaConta para,
        string motivo,
        string origem,
        DateTimeOffset ocorridaEm)
    {
        Id = Guid.CreateVersion7();
        ContaId = contaId;
        Sequencia = sequencia;
        De = de;
        Para = para;
        Motivo = motivo;
        Origem = origem;
        OcorridaEm = ocorridaEm;
    }

    public Guid Id { get; private set; }

    public Guid ContaId { get; private set; }

    public long Sequencia { get; private set; }

    public EstadoDaConta De { get; private set; }

    public EstadoDaConta Para { get; private set; }

    /// <summary>Por que mudou. Obrigatorio — bloqueio sem motivo nao se audita.</summary>
    public string Motivo { get; private set; } = string.Empty;

    public string Origem { get; private set; } = string.Empty;

    public DateTimeOffset OcorridaEm { get; private set; }
}
