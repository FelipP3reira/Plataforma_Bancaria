using Banco.Dominio.Erros;

namespace Banco.Aplicacao.Erros;

/// <summary>Conta que a operacao pediu e que nao existe.</summary>
public sealed class ContaNaoEncontradaException : DominioException
{
    public ContaNaoEncontradaException(string mensagem) : base(mensagem)
    {
    }

    public ContaNaoEncontradaException(string mensagem, Exception causa) : base(mensagem, causa)
    {
    }
}
