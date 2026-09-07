using Banco.Dominio.Erros;

namespace Banco.Aplicacao.Erros;

/// <summary>Mesma chave, conteudo diferente — erro do cliente, nao reenvio.</summary>
public sealed class ChaveDeIdempotenciaReutilizadaException : DominioException
{
    public ChaveDeIdempotenciaReutilizadaException(string mensagem) : base(mensagem)
    {
    }

    public ChaveDeIdempotenciaReutilizadaException(string mensagem, Exception causa) : base(mensagem, causa)
    {
    }
}
