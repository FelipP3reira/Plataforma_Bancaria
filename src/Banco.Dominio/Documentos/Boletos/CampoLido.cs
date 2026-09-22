namespace Banco.Dominio.Documentos.Boletos;

/// <summary>
/// Valores fixados: o nome do campo e persistido como inteiro, e reordenar o enum reescreveria
/// o que ja esta gravado.
/// </summary>
public enum NomeDoCampo
{
    LinhaDigitavel = 1,
    Valor = 2,
    Vencimento = 3,
    CnpjDoEmissor = 4,
}

/// <summary>De onde o valor veio, que e o que explica a confianca dele.</summary>
public enum OrigemDoCampo
{
    /// <summary>
    /// Saiu de uma estrutura que se confere sozinha — linha digitavel com os quatro digitos
    /// fechando, CNPJ com os dois verificadores batendo.
    /// </summary>
    Estrutura = 1,

    /// <summary>
    /// Saiu do texto solto, por formato. Ninguem confirma que o numero achado e o campo que
    /// se procurava: "R$ 45,00" pode ser o valor do boleto ou o da multa impressa ao lado.
    /// </summary>
    Texto = 2,
}

/// <summary>
/// Um campo lido do documento, com o quanto se pode confiar nele e por que.
/// </summary>
/// <param name="Valor">
/// A forma canonica: algarismos para linha digitavel e CNPJ, <c>0.00</c> invariante para valor,
/// <c>aaaa-mm-dd</c> para data. Texto e nao tipo porque a tabela guarda campos de tipos
/// diferentes na mesma coluna, e porque a correcao humana da fatia seguinte tambem chega como
/// texto.
/// </param>
/// <param name="Observacao">
/// Por que a confianca e o que e, quando ela nao e cheia. Nunca carrega o conteudo do
/// documento — e o tipo de campo que acaba em log e em tela de suporte.
/// </param>
public sealed record CampoLido(
    NomeDoCampo Nome,
    string Valor,
    decimal Confianca,
    OrigemDoCampo Origem,
    string? Observacao = null);

/// <summary>
/// O que se conseguiu ler de um documento, e o quanto o conjunto merece confianca.
/// </summary>
/// <param name="Confianca">
/// O menor valor entre os campos necessarios para pagar. E o minimo e nao a media de
/// proposito: media deixa um campo ilegivel passar escondido atras de tres campos perfeitos, e
/// o campo ilegivel pode ser justamente o valor.
/// </param>
public sealed record LeituraDoDocumento(IReadOnlyList<CampoLido> Campos, decimal Confianca)
{
    public static LeituraDoDocumento Nada => new([], 0m);

    public CampoLido? Campo(NomeDoCampo nome) =>
        Campos.FirstOrDefault(campo => campo.Nome == nome);
}
