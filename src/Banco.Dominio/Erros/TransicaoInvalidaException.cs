namespace Banco.Dominio.Erros;

/// <summary>Mudanca de estado que a maquina de estados da conta nao aceita.</summary>
public sealed class TransicaoInvalidaException : DominioException
{
    public TransicaoInvalidaException(string mensagem) : base(mensagem)
    {
    }

    public TransicaoInvalidaException(string mensagem, Exception causa) : base(mensagem, causa)
    {
    }
}
