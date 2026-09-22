namespace Banco.Dominio.Erros;

/// <summary>O arquivo enviado nao passa na porta: tipo, tamanho ou conteudo.</summary>
public sealed class ArquivoRecusadoException : DominioException
{
    public ArquivoRecusadoException(string mensagem) : base(mensagem)
    {
    }

    public ArquivoRecusadoException(string mensagem, Exception causa) : base(mensagem, causa)
    {
    }
}
