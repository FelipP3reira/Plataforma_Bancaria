using Banco.Dominio.Comum;
using Banco.Dominio.Contas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banco.Infraestrutura.Persistencia.Configuracoes;

internal sealed class ContaConfiguracao : IEntityTypeConfiguration<Conta>
{
    public void Configure(EntityTypeBuilder<Conta> conta)
    {
        ArgumentNullException.ThrowIfNull(conta);

        conta.ToTable("Contas");
        conta.HasKey(linha => linha.Id);

        // O Id sai do GUID v7 gerado no construtor, nunca do banco. Sem dizer isso ao EF,
        // ele trata chave Guid como gerada na insercao e passa a usar a heuristica "chave
        // preenchida significa registro existente" — o que transforma INSERT em UPDATE.
        conta.Property(linha => linha.Id).ValueGeneratedNever();

        conta.Property(linha => linha.Numero)
            .HasConversion(ConversoresDeDominio.NumeroDaConta)
            .HasMaxLength(9)
            .IsRequired();

        conta.HasIndex(linha => linha.Numero)
            .IsUnique()
            .HasDatabaseName("IX_Contas_Numero");

        conta.Property(linha => linha.Titular)
            .HasMaxLength(Conta.TamanhoMaximoDoTitular)
            .IsRequired();

        // 18,2 e nao 18,4: o dominio ja recusa a terceira casa, e deixar a coluna mais
        // larga do que o tipo aceita criaria um jeito de entrar dinheiro que o codigo
        // nao consegue produzir — e que a reconciliacao acusaria sem explicar de onde veio.
        conta.Property(linha => linha.Saldo)
            .HasConversion(ConversoresDeDominio.Dinheiro)
            .HasPrecision(18, Dinheiro.CasasDecimais)
            .IsRequired();

        conta.Property(linha => linha.UltimaSequencia).IsRequired();

        // O agregado nao carrega o ledger: uma conta com dez mil lancamentos nao pode ler
        // dez mil linhas para gravar a proxima. Por isso nao ha navegacao daqui para
        // Lancamentos — o vinculo existe so do lado do lancamento, pela chave estrangeira.
    }
}
