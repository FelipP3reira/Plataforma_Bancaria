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
    /// </remarks>
    [Required]
    public string Raiz { get; init; } = string.Empty;
}
