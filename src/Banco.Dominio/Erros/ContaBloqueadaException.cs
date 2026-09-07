namespace Banco.Dominio.Erros;

/// <summary>Movimentacao pedida numa conta que nao esta ativa.</summary>
public sealed class ContaBloqueadaException : DominioException
{
    public ContaBloqueadaException(string mensagem) : base(mensagem)
    {
    }

    public ContaBloqueadaException(string mensagem, Exception causa) : base(mensagem, causa)
    {
    }
}
