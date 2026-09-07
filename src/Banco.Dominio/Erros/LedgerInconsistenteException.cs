namespace Banco.Dominio.Erros;

/// <summary>O ledger e o saldo materializado divergiram.</summary>
public sealed class LedgerInconsistenteException : DominioException
{
    public LedgerInconsistenteException(string mensagem) : base(mensagem)
    {
    }

    public LedgerInconsistenteException(string mensagem, Exception causa) : base(mensagem, causa)
    {
    }
}
