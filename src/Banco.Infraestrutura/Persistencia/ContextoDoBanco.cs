using Banco.Dominio.Comum;
using Banco.Dominio.Contas;
using Banco.Dominio.Ledger;
using Banco.Dominio.Transferencias;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infraestrutura.Persistencia;

public sealed class ContextoDoBanco : DbContext
{
    /// <summary>Sequencia que gera a parte numerica do numero de conta.</summary>
    public const string SequenciaDeContas = "SequenciaDeContas";

    public ContextoDoBanco(DbContextOptions<ContextoDoBanco> opcoes) : base(opcoes)
    {
    }

    public DbSet<Conta> Contas => Set<Conta>();

    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();

    public DbSet<Transferencia> Transferencias => Set<Transferencia>();

    public DbSet<MudancaDeEstadoDaConta> MudancasDeEstadoDaConta => Set<MudancaDeEstadoDaConta>();

    // Nome do parametro imposto pela assinatura da classe base.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Sequencia do banco, e nao contador na aplicacao: dois processos gerando numero
        // de conta ao mesmo tempo gerariam o mesmo, e o indice unico so avisaria depois
        // de um cliente ja ter recebido a resposta.
        modelBuilder.HasSequence<long>(SequenciaDeContas).StartsAt(1).IncrementsBy(1);

        // Sem chave e sem tabela: e o formato de uma consulta, nao de algo gravado.
        modelBuilder.Entity<LinhaDaConciliacao>(linha =>
        {
            linha.HasNoKey().ToView(null);

            // Precisao explicita mesmo sem tabela: sem ela o EF assume o padrao ao ler a
            // coluna, e um saldo grande voltaria truncado justamente na consulta cujo
            // trabalho e detectar valor errado.
            linha.Property(valor => valor.SaldoMaterializado).HasPrecision(18, Dinheiro.CasasDecimais);
            linha.Property(valor => valor.SomaDoLedger).HasPrecision(18, Dinheiro.CasasDecimais);
        });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContextoDoBanco).Assembly);
    }
}
