namespace Banco.Dominio.Erros;

/// <summary>Operacao que a conta nao aceita no estado em que esta.</summary>
public sealed class ContaInvalidaException : DominioException
{
    public ContaInvalidaException(string mensagem) : base(mensagem)
    {
    }

    public ContaInvalidaException(string mensagem, Exception causa) : base(mensagem, causa)
    {
    }
}
