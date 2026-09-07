using System.Data;
using Banco.Aplicacao.Portas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Banco.Infraestrutura.Persistencia;

public sealed class UnidadeDeTrabalho : IUnidadeDeTrabalho
{
    private readonly ContextoDoBanco contexto;

    public UnidadeDeTrabalho(ContextoDoBanco contexto) => this.contexto = contexto;

    /// <remarks>
    /// <c>READ COMMITTED</c>, o padrao, e nao <c>SERIALIZABLE</c>. O que este sistema
    /// precisa proteger e uma linha especifica sendo lida e regravada, e para isso a trava
    /// explicita da leitura basta. <c>SERIALIZABLE</c> resolveria o mesmo caso pegando
    /// travas de faixa em tudo que a transacao encostar, encarecendo toda consulta de
    /// extrato que rodasse junto para proteger contra leitura fantasma — que aqui nao
    /// existe, porque ninguem decide nada a partir de um conjunto de linhas.
    /// </remarks>
    public async Task<ITransacao> Abrir(CancellationToken cancelamento) =>
        new Transacao(
            await contexto.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancelamento)
                .ConfigureAwait(false));

    public Task Salvar(CancellationToken cancelamento) => contexto.SaveChangesAsync(cancelamento);

    private sealed class Transacao : ITransacao
    {
        private readonly IDbContextTransaction transacao;

        public Transacao(IDbContextTransaction transacao) => this.transacao = transacao;

        public Task Confirmar(CancellationToken cancelamento) => transacao.CommitAsync(cancelamento);

        // Sem Confirmar, o descarte desfaz. Nao ha Reverter explicito: esquecer de chamar
        // um rollback e mais facil do que esquecer um using, e o resultado do esquecimento
        // seria uma transacao aberta segurando a trava da conta.
        public ValueTask DisposeAsync() => transacao.DisposeAsync();
    }
}
