namespace Banco.Dominio.Erros;

/// <summary>O boleto lido nao fecha: formato, digito verificador ou campo fora da faixa.</summary>
public sealed class BoletoInvalidoException : DominioException
{
    public BoletoInvalidoException(string mensagem) : base(mensagem)
    {
    }

    public BoletoInvalidoException(string mensagem, Exception causa) : base(mensagem, causa)
    {
    }
}
