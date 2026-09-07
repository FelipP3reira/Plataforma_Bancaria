using Banco.Dominio.Erros;

namespace Banco.Aplicacao.Erros;

/// <summary>Filtro, tamanho de pagina ou marcador que a consulta nao aceita.</summary>
public sealed class ConsultaInvalidaException : DominioException
{
    public ConsultaInvalidaException(string mensagem) : base(mensagem)
    {
    }

    public ConsultaInvalidaException(string mensagem, Exception causa) : base(mensagem, causa)
    {
    }
}
