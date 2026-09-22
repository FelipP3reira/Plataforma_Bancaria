using System.ComponentModel.DataAnnotations;

namespace Banco.Infraestrutura.Documentos;

/// <summary>Onde os documentos ficam em disco.</summary>
public sealed class OpcoesDeArmazenamento
{
    public const string Secao = "Documentos";

    /// <summary>
    /// Pasta raiz dos arquivos, fora do diretorio servido pela aplicacao.
    /// </summary>
    /// <remarks>
    /// Documento financeiro dentro de <c>wwwroot</c> vira URL publica: quem descobrir o
    /// nome baixa o boleto de outra pessoa sem passar por lugar nenhum do codigo. Aqui o
    /// unico jeito de ler o arquivo e por uma rota, que e onde a autorizacao mora — ou
    /// moraria, enquanto ela nao existe.
    /// <para>
    /// <b>Precisa ser caminho absoluto</b>, e a conferencia esta em
    /// <see cref="ArmazenamentoEmDisco"/>. Caminho relativo funciona enquanto existe um
    /// processo so e quebra quando o worker sobe: a API resolve <c>./dados</c> contra a pasta
    /// dela e o worker contra a pasta dele, e o arquivo que um grava o outro nao acha. Foi
    /// exatamente isso que aconteceu na primeira subida dos dois juntos.
    /// </para>
    /// </remarks>
    [Required]
    public string Raiz { get; init; } = string.Empty;
}
