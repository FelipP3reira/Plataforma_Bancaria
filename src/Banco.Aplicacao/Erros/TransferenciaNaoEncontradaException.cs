using Banco.Dominio.Erros;

namespace Banco.Aplicacao.Erros;

/// <summary>Transferencia que a consulta pediu e que nao existe.</summary>
public sealed class TransferenciaNaoEncontradaException : DominioException
{
    public TransferenciaNaoEncontradaException(string mensagem) : base(mensagem)
    {
    }

    public TransferenciaNaoEncontradaException(string mensagem, Exception causa) : base(mensagem, causa)
    {
    }
}
