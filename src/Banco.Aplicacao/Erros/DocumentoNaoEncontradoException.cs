using Banco.Dominio.Erros;

namespace Banco.Aplicacao.Erros;

public sealed class DocumentoNaoEncontradoException : DominioException
{
    public DocumentoNaoEncontradoException(string mensagem) : base(mensagem)
    {
    }

    public DocumentoNaoEncontradoException(string mensagem, Exception causa) : base(mensagem, causa)
    {
    }
}
