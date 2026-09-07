using Banco.Dominio.Contas;
using Banco.Dominio.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banco.Infraestrutura.Persistencia.Configuracoes;

internal sealed class MudancaDeEstadoDaContaConfiguracao : IEntityTypeConfiguration<MudancaDeEstadoDaConta>
{
    public void Configure(EntityTypeBuilder<MudancaDeEstadoDaConta> mudanca)
    {
        ArgumentNullException.ThrowIfNull(mudanca);

        mudanca.ToTable("MudancasDeEstadoDaConta");
        mudanca.HasKey(linha => linha.Id);
        mudanca.Property(linha => linha.Id).ValueGeneratedNever();

        mudanca.Property(linha => linha.De).HasConversion<int>();
        mudanca.Property(linha => linha.Para).HasConversion<int>();

        mudanca.Property(linha => linha.Motivo)
            .HasMaxLength(MudancaDeEstadoDaConta.TamanhoMaximoDoMotivo)
            .IsRequired();

        mudanca.Property(linha => linha.Origem)
            .HasMaxLength(PedidoDeLancamento.TamanhoMaximoDaOrigem)
            .IsRequired();

        // Mesmo motivo do ledger: e a rede embaixo da trava. Duas mudancas concorrentes que
        // leram o mesmo estado tentariam gravar a mesma posicao, e o banco recusa a segunda.
        mudanca.HasIndex(linha => new { linha.ContaId, linha.Sequencia })
            .IsUnique()
            .HasDatabaseName("IX_MudancasDeEstadoDaConta_ContaId_Sequencia");

        mudanca.HasOne<Conta>()
            .WithMany()
            .HasForeignKey(linha => linha.ContaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
