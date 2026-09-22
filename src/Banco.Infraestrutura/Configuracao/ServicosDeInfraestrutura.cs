using Banco.Aplicacao.Documentos;
using Banco.Aplicacao.Portas;
using Banco.Infraestrutura.Documentos;
using Banco.Infraestrutura.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Banco.Infraestrutura.Configuracao;

/// <summary>
/// O que a API e o worker precisam em comum: banco, armazenamento e extrator.
/// </summary>
/// <remarks>
/// Compartilhado porque sao dois processos sobre o mesmo banco. Cada um registrando o seu
/// <c>DbContext</c> por conta propria funcionaria ate a primeira divergencia de configuracao
/// — e uma diferenca de nivel de isolamento entre quem grava e quem le a fila e o tipo de
/// bug que so aparece sob carga.
/// </remarks>
public static class ServicosDeInfraestrutura
{
    public static IServiceCollection AdicionarInfraestrutura(
        this IServiceCollection servicos,
        IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(servicos);

        // A string de conexao e lida do provedor, nao capturada aqui. Configuracao lida no
        // momento do registro congela o valor que existia antes de as fontes adicionadas
        // depois entrarem — e e exatamente isso que a fabrica dos testes de integracao faz
        // para apontar a API ao banco em container.
        servicos.AddDbContext<ContextoDoBanco>((provedor, opcoes) =>
            opcoes.UseSqlServer(
                provedor.GetRequiredService<IConfiguration>().GetConnectionString("Banco")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Banco nao configurada. Veja o .env.example.")));

        servicos.AddSingleton(TimeProvider.System);

        servicos.AddScoped<IRepositorioDeContas, RepositorioDeContas>();
        servicos.AddScoped<IConciliacaoDeLedger, ConciliacaoDeLedger>();
        servicos.AddScoped<IExtratoDaConta, ExtratoDaConta>();
        servicos.AddScoped<IRepositorioDeDocumentos, RepositorioDeDocumentos>();
        servicos.AddScoped<IRepositorioDeTransferencias, RepositorioDeTransferencias>();
        servicos.AddScoped<IUnidadeDeTrabalho, UnidadeDeTrabalho>();

        servicos.AddSingleton<IArmazenamentoDeDocumentos, ArmazenamentoEmDisco>();
        servicos.AddSingleton<IExtratorDeDocumento, ExtratorDeEnsaio>();

        // Validada na subida: pasta de documentos errada descoberta no primeiro upload seria
        // um arquivo perdido em producao para dizer o que a configuracao ja dizia.
        servicos.AddOptions<OpcoesDeArmazenamento>()
            .Bind(configuracao.GetSection(OpcoesDeArmazenamento.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos.AddOptions<OpcoesDoPipeline>()
            .Bind(configuracao.GetSection(OpcoesDoPipeline.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Resolvido para o valor, e nao para IOptions: assim a camada de aplicacao recebe as
        // opcoes sem passar a depender do pacote de configuracao.
        servicos.AddSingleton(provedor =>
            provedor.GetRequiredService<IOptions<OpcoesDoPipeline>>().Value);

        return servicos;
    }
}
