namespace Banco.Dominio.Erros;

/// <summary>
/// Raiz das violacoes de regra de dominio. A API traduz tudo que herda daqui em 4xx —
/// sao erros do pedido, nao falha do servidor.
/// </summary>
public abstract class DominioException : Exception
{
    protected DominioException(string mensagem) : base(mensagem)
    {
    }

    protected DominioException(string mensagem, Exception causa) : base(mensagem, causa)
    {
    }
}
