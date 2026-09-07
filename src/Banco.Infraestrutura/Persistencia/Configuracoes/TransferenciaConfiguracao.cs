using Banco.Dominio.Comum;
using Banco.Dominio.Contas;
using Banco.Dominio.Ledger;
using Banco.Dominio.Transferencias;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banco.Infraestrutura.Persistencia.Configuracoes;

internal sealed class TransferenciaConfiguracao : IEntityTypeConfiguration<Transferencia>
{
    public void Configure(EntityTypeBuilder<Transferencia> transferencia)
    {
        ArgumentNullException.ThrowIfNull(transferencia);

        transferencia.ToTable("Transferencias");
        transferencia.HasKey(linha => linha.Id);
        transferencia.Property(linha => linha.Id).ValueGeneratedNever();

        transferencia.Property(linha => linha.Valor)
            .HasConversion(ConversoresDeDominio.Dinheiro)
            .HasPrecision(18, Dinheiro.CasasDecimais)
            .IsRequired();

        transferencia.Property(linha => linha.Descricao)
            .HasMaxLength(Lancamento.TamanhoMaximoDaDescricao)
            .IsRequired();

        transferencia.Property(linha => linha.Origem)
            .HasMaxLength(PedidoDeLancamento.TamanhoMaximoDaOrigem)
            .IsRequired();

        transferencia.Property(linha => linha.ChaveIdempotencia)
            .HasMaxLength(PedidoDeLancamento.TamanhoMaximoDaChave)
            .IsRequired();

        // A chave vale dentro da conta de origem, como no deposito e no saque: e quem paga
        // que manda a chave, e dois pagadores diferentes podem usar a mesma sem que uma
        // transferencia seja reenvio da outra.
        transferencia.HasIndex(linha => new { linha.ContaOrigemId, linha.ChaveIdempotencia })
            .IsUnique()
            .HasDatabaseName("IX_Transferencias_ContaOrigemId_ChaveIdempotencia");

        transferencia.HasIndex(linha => linha.ContaDestinoId)
            .HasDatabaseName("IX_Transferencias_ContaDestinoId");

        foreach (var conta in new[] { nameof(Transferencia.ContaOrigemId), nameof(Transferencia.ContaDestinoId) })
        {
            transferencia.HasOne<Conta>()
                .WithMany()
                .HasForeignKey(conta)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
