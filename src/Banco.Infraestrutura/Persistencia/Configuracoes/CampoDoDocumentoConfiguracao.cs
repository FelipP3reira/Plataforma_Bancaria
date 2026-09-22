using Banco.Dominio.Comum;
using Banco.Dominio.Documentos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banco.Infraestrutura.Persistencia.Configuracoes;

internal sealed class CampoDoDocumentoConfiguracao : IEntityTypeConfiguration<CampoDoDocumento>
{
    public void Configure(EntityTypeBuilder<CampoDoDocumento> campo)
    {
        ArgumentNullException.ThrowIfNull(campo);

        campo.ToTable("CamposDoDocumento");
        campo.HasKey(linha => linha.Id);
        campo.Property(linha => linha.Id).ValueGeneratedNever();

        campo.Property(linha => linha.Nome).HasConversion<int>();
        campo.Property(linha => linha.Origem).HasConversion<int>();

        campo.Property(linha => linha.ValorLido)
            .HasMaxLength(CampoDoDocumento.TamanhoMaximoDoValor)
            .IsRequired();

        campo.Property(linha => linha.Observacao)
            .HasMaxLength(CampoDoDocumento.TamanhoMaximoDaObservacao);

        campo.Property(linha => linha.Confianca).HasPrecision(5, Dinheiro.CasasDecimais + 2);

        // Um campo por nome, por documento. O indice unico e o que garante que a tela de
        // revisao nao encontre duas linhas de "Valor" dizendo coisas diferentes.
        campo.HasIndex(linha => new { linha.DocumentoId, linha.Nome })
            .IsUnique()
            .HasDatabaseName("IX_CamposDoDocumento_DocumentoId_Nome");
    }
}
