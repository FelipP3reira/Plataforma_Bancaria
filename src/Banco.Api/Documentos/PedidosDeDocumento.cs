namespace Banco.Api.Documentos;

/// <param name="Correcoes">
/// Nome do campo para o valor corrigido — por exemplo <c>{"Valor": "1.402,77"}</c>. Campo que nao
/// aparece fica como a maquina leu.
/// </param>
public sealed record RevisaoHttp(Dictionary<string, string>? Correcoes)
{
    public Dictionary<string, string> Correcoes { get; init; } = Correcoes ?? [];
}
