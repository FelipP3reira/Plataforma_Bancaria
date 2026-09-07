using Banco.Dominio.Comum;
using Banco.Dominio.Contas;
using Banco.Dominio.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banco.Infraestrutura.Persistencia.Configuracoes;

internal sealed class LancamentoConfiguracao : IEntityTypeConfiguration<Lancamento>
{
    public void Configure(EntityTypeBuilder<Lancamento> lancamento)
    {
        ArgumentNullException.ThrowIfNull(lancamento);

        lancamento.ToTable("Lancamentos");
        lancamento.HasKey(linha => linha.Id);
        lancamento.Property(linha => linha.Id).ValueGeneratedNever();

        lancamento.Property(linha => linha.ContaId).IsRequired();
        lancamento.Property(linha => linha.Sequencia).IsRequired();
        lancamento.Property(linha => linha.Tipo).HasConversion<int>();

        lancamento.Property(linha => linha.Valor)
            .HasConversion(ConversoresDeDominio.Dinheiro)
            .HasPrecision(18, Dinheiro.CasasDecimais)
            .IsRequired();

        lancamento.Property(linha => linha.SaldoDepois)
            .HasConversion(ConversoresDeDominio.Dinheiro)
            .HasPrecision(18, Dinheiro.CasasDecimais)
            .IsRequired();

        lancamento.Property(linha => linha.Descricao)
            .HasMaxLength(Lancamento.TamanhoMaximoDaDescricao)
            .IsRequired();

        lancamento.Property(linha => linha.Origem)
            .HasMaxLength(PedidoDeLancamento.TamanhoMaximoDaOrigem)
            .IsRequired();

        lancamento.Property(linha => linha.ChaveIdempotencia)
            .HasMaxLength(PedidoDeLancamento.TamanhoMaximoDaChave)
            .IsRequired();

        lancamento.Property(linha => linha.CriadoEm).IsRequired();

        // Nulo na maioria das linhas: so as pernas de transferencia apontam para alguma.
        // Indice filtrado por isso — indexar as nulas seria pagar espaco e escrita pelas
        // linhas que a consulta nunca procura.
        lancamento.HasIndex(linha => linha.TransferenciaId)
            .HasFilter("[TransferenciaId] IS NOT NULL")
            .HasDatabaseName("IX_Lancamentos_TransferenciaId");

        // A rede embaixo da trava. Se a trava pessimista falhar — hint esquecido em algum
        // caminho novo, transacao aberta no nivel errado —, duas gravacoes concorrentes
        // que leram o mesmo saldo tentam gravar a mesma posicao, e o banco recusa a
        // segunda. Sem este indice, a falha da trava vira dinheiro duplicado em silencio.
        lancamento.HasIndex(linha => new { linha.ContaId, linha.Sequencia })
            .IsUnique()
            .HasDatabaseName("IX_Lancamentos_ContaId_Sequencia");

        // Idempotencia por conta, e nao global: duas contas podem legitimamente receber
        // pedidos com a mesma chave de clientes diferentes.
        lancamento.HasIndex(linha => new { linha.ContaId, linha.ChaveIdempotencia })
            .IsUnique()
            .HasDatabaseName("IX_Lancamentos_ContaId_ChaveIdempotencia");

        // Extrato com filtro de periodo. Sem ele, filtrar por data numa conta com milhares
        // de lancamentos vira varredura de tudo que a conta ja teve.
        //
        // A sequencia entra como terceira coluna, e nao como coluna incluida: ela e o
        // desempate do marcador de pagina, e o banco so consegue continuar a busca de onde
        // parou se a ordem do indice for a mesma ordem do extrato. Como coluna incluida ela
        // seria lida, mas nao ordenada, e cada pagina custaria uma ordenacao do periodo.
        lancamento.HasIndex(linha => new { linha.ContaId, linha.CriadoEm, linha.Sequencia })
            .HasDatabaseName("IX_Lancamentos_ContaId_CriadoEm_Sequencia");

        lancamento.HasOne<Conta>()
            .WithMany()
            .HasForeignKey(linha => linha.ContaId)

            // Ledger nao se apaga em cascata. Conta encerrada continua tendo extrato: a
            // obrigacao de guardar o historico nao acaba quando o cliente vai embora.
            .OnDelete(DeleteBehavior.Restrict);
    }
}
