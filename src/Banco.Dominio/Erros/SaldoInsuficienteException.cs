namespace Banco.Dominio.Erros;

/// <summary>Debito maior que o saldo disponivel.</summary>
public sealed class SaldoInsuficienteException : DominioException
{
    public SaldoInsuficienteException(string mensagem) : base(mensagem)
    {
    }

    public SaldoInsuficienteException(string mensagem, Exception causa) : base(mensagem, causa)
    {
    }
}
