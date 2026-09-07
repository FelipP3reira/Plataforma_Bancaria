namespace Banco.Aplicacao.Portas;

/// <summary>
/// A transacao do caso de uso.
/// </summary>
/// <remarks>
/// Explicita, e nao escondida atras de um "Salvar" do repositorio, porque neste sistema a
/// transacao e regra de negocio: transferencia debita uma conta e credita outra, e ou as
/// duas linhas entram ou nenhuma entra. Um repositorio que salvasse sozinho tiraria de quem
/// le o caso de uso a informacao de onde a transacao comeca e termina.
/// <para>
/// A trava pessimista da leitura tambem depende disto: ela vale ate o commit, entao quem
/// abre a transacao e quem define ate quando a linha fica segura.
/// </para>
/// </remarks>
public interface IUnidadeDeTrabalho
{
    Task<ITransacao> Abrir(CancellationToken cancelamento);

    Task Salvar(CancellationToken cancelamento);
}

public interface ITransacao : IAsyncDisposable
{
    Task Confirmar(CancellationToken cancelamento);
}
