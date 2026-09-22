using Banco.Dominio.Documentos.Boletos;
using Banco.Dominio.Erros;

namespace Banco.Dominio.Documentos;

/// <summary>
/// Um campo extraido de um documento, com a confianca que a origem dele justifica.
/// </summary>
/// <remarks>
/// Tabela filha, e nao colunas no documento. Tres motivos concretos: o conjunto de campos
/// cresce (linha digitavel hoje, nosso numero e agencia depois) e coluna nova por campo faria a
/// tabela principal crescer junto; a confianca e por campo, e um par de colunas por campo
/// dobraria a largura; e a correcao humana precisa morar <em>ao lado</em> do valor lido, nao no
/// lugar dele — o que exige uma linha com as duas coisas.
/// </remarks>
public sealed class CampoDoDocumento
{
    public const int TamanhoMaximoDoValor = 60;
    public const int TamanhoMaximoDaObservacao = 200;
    public const int TamanhoMaximoDoRevisor = 100;

    private CampoDoDocumento()
    {
    }

    private CampoDoDocumento(
        Guid documentoId,
        NomeDoCampo nome,
        string valorLido,
        decimal confianca,
        OrigemDoCampo origem,
        string? observacao)
    {
        Id = Guid.CreateVersion7();
        DocumentoId = documentoId;
        Nome = nome;
        ValorLido = valorLido;
        Confianca = confianca;
        Origem = origem;
        Observacao = observacao;
    }

    public Guid Id { get; private set; }

    public Guid DocumentoId { get; private set; }

    public NomeDoCampo Nome { get; private set; }

    /// <summary>
    /// O valor na forma canonica, como saiu da leitura.
    /// </summary>
    /// <remarks>
    /// Nunca se altera. Quando alguem corrige o campo, a correcao entra em outra coluna e esta
    /// continua dizendo o que a maquina leu — sem isso, nao ha como medir depois em que campos a
    /// extracao erra, que e a informacao que diz onde vale a pena melhorar.
    /// </remarks>
    public string ValorLido { get; private set; } = string.Empty;

    public decimal Confianca { get; private set; }

    public OrigemDoCampo Origem { get; private set; }

    /// <summary>Por que a confianca e o que e. Nunca carrega conteudo do documento.</summary>
    public string? Observacao { get; private set; }

    /// <summary>
    /// O que uma pessoa corrigiu, quando corrigiu. Nulo enquanto ninguem mexeu.
    /// </summary>
    /// <remarks>
    /// Ao lado do valor lido, e nao no lugar dele. Sobrescrever apagaria o unico registro do que
    /// a maquina errou — e e esse registro que responde depois em que campo a extracao erra mais,
    /// que e a informacao que diz onde vale a pena melhorar o extrator.
    /// </remarks>
    public string? ValorCorrigido { get; private set; }

    public string? CorrigidoPor { get; private set; }

    public DateTimeOffset? CorrigidoEm { get; private set; }

    /// <summary>O valor que vale: o corrigido quando existe, o lido quando nao.</summary>
    /// <remarks>
    /// Todo consumidor le por aqui. Quem lesse <see cref="ValorLido"/> direto pagaria o valor
    /// errado justamente nos documentos que passaram pela revisao — os que mais precisavam de
    /// cuidado.
    /// </remarks>
    public string ValorFinal => ValorCorrigido ?? ValorLido;

    public bool FoiCorrigido => ValorCorrigido is not null;

    /// <summary>Troca o valor deste campo, guardando o que a maquina tinha lido.</summary>
    internal void Corrigir(string valor, string revisor, DateTimeOffset agora)
    {
        // A canonizacao recusa o que nao serve para este campo. A correcao e a unica porta por
        // onde um valor entra sem ter passado pelo verificador da leitura automatica: linha
        // digitavel corrigida para qualquer coisa fecharia o documento e o pagamento cobraria o
        // que foi digitado.
        var canonico = ValorDeCampo.Canonizar(Nome, valor);

        if (canonico.Length > TamanhoMaximoDoValor)
        {
            throw new BoletoInvalidoException($"Valor de {Nome} longo demais.");
        }

        ValorCorrigido = canonico;
        CorrigidoPor = revisor;
        CorrigidoEm = agora;
    }

    internal static CampoDoDocumento De(Guid documentoId, CampoLido lido) =>
        new(
            documentoId,
            lido.Nome,
            Encurtar(lido.Valor, TamanhoMaximoDoValor),
            lido.Confianca,
            lido.Origem,
            lido.Observacao is null ? null : Encurtar(lido.Observacao, TamanhoMaximoDaObservacao));

    private static string Encurtar(string texto, int limite) =>
        texto.Length > limite ? texto[..limite] : texto;
}
