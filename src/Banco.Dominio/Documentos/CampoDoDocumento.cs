using Banco.Dominio.Documentos.Boletos;

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
