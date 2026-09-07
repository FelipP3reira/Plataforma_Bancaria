namespace Banco.Dominio.Erros;

/// <summary>Valor monetario fora do que o dominio aceita.</summary>
public sealed class ValorInvalidoException : DominioException
{
    public ValorInvalidoException(string mensagem) : base(mensagem)
    {
    }

    public ValorInvalidoException(string mensagem, Exception causa) : base(mensagem, causa)
    {
    }
}
