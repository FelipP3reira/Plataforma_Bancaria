using Banco.Dominio.Contas;
using Banco.Dominio.Documentos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banco.Infraestrutura.Persistencia.Configuracoes;

internal sealed class DocumentoConfiguracao : IEntityTypeConfiguration<Documento>
{
    private const int TamanhoMaximoDoCaminho = 300;
    private const int TamanhoMaximoDaOrigem = 100;

    public void Configure(EntityTypeBuilder<Documento> documento)
    {
        ArgumentNullException.ThrowIfNull(documento);

        documento.ToTable("Documentos");
        documento.HasKey(linha => linha.Id);
        documento.Property(linha => linha.Id).ValueGeneratedNever();

        documento.Property(linha => linha.ContaId).IsRequired();

        documento.Property(linha => linha.NomeOriginal)
            .HasMaxLength(Documento.TamanhoMaximoDoNome)
            .IsRequired();

        documento.Property(linha => linha.Tipo).HasConversion<int>();
        documento.Property(linha => linha.Estado).HasConversion<int>();
        documento.Property(linha => linha.TamanhoEmBytes).IsRequired();

        documento.Property(linha => linha.Hash)
            .HasConversion(ConversoresDeDominio.Hash)
            .HasMaxLength(HashDoArquivo.TamanhoEmCaracteres)
            .IsRequired();

        documento.Property(linha => linha.CaminhoRelativo)
            .HasMaxLength(TamanhoMaximoDoCaminho)
            .IsRequired();

        documento.Property(linha => linha.Origem)
            .HasMaxLength(TamanhoMaximoDaOrigem)
            .IsRequired();

        documento.Property(linha => linha.RecebidoEm).IsRequired();
        documento.Property(linha => linha.AtualizadoEm).IsRequired();

        documento.Property(linha => linha.Tentativas).IsRequired();
        documento.Property(linha => linha.UltimoErro).HasMaxLength(Documento.TamanhoMaximoDoErro);

        // Sem limite de tamanho, ao contrario de todo o resto: e texto de documento, e
        // cortar no meio estragaria justamente o campo que o parser precisa ler.
        documento.Property(linha => linha.ConteudoExtraido);

        // Quatro casas para um numero entre 0 e 1. Sem precisao explicita o SQL Server
        // assume decimal(18,2), e toda confianca viraria 0,00 / 0,50 / 1,00 — o limiar de
        // revisao manual passaria a comparar contra um valor arredondado.
        documento.Property(linha => linha.Confianca).HasPrecision(5, 4);

        // A rede embaixo da conferencia de reenvio. Se dois uploads do mesmo arquivo
        // chegarem juntos, os dois passam pela consulta por hash e o banco recusa o
        // segundo — em vez de a conta ficar com duas extracoes do mesmo boleto.
        documento.HasIndex(linha => new { linha.ContaId, linha.Hash })
            .IsUnique()
            .HasDatabaseName("IX_Documentos_ContaId_Hash");

        // A fila do worker: os mais antigos em cada estado, primeiro.
        documento.HasIndex(linha => new { linha.Estado, linha.RecebidoEm })
            .HasDatabaseName("IX_Documentos_Estado_RecebidoEm");

        // A varredura de reserva vencida roda a cada rodada do worker e quase sempre nao
        // acha nada. Sem indice, esse "nada" custaria uma varredura da tabela inteira a
        // cada poucos segundos.
        documento.HasIndex(linha => new { linha.Estado, linha.LeaseAte })
            .HasDatabaseName("IX_Documentos_Estado_LeaseAte");

        documento.HasOne<Conta>()
            .WithMany()
            .HasForeignKey(linha => linha.ContaId)

            // Documento nao se apaga em cascata pelo mesmo motivo do ledger: conta
            // encerrada continua tendo comprovante do que foi pago por ela.
            .OnDelete(DeleteBehavior.Restrict);
    }
}
