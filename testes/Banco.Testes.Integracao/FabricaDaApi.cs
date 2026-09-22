using System.Globalization;
using Banco.Aplicacao.Documentos;
using Banco.Aplicacao.Portas;
using Banco.Dominio.Documentos;
using Banco.Infraestrutura.Documentos;
using Banco.Infraestrutura.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Banco.Testes.Integracao;

/// <summary>
/// Sobe a API contra um SQL Server de verdade em container.
/// </summary>
/// <remarks>
/// Banco real, e nao em memoria, porque quase tudo que estes testes provam so existe no
/// banco: a trava <c>UPDLOCK</c> que serializa dois saques, o indice unico que segura a
/// posicao do ledger, a transacao que faz duas linhas entrarem juntas ou nenhuma entrar.
/// Provedor em memoria nao tem nada disso — passaria em tudo sem provar nada.
/// </remarks>
public sealed class FabricaDaApi : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Mesma imagem do docker-compose: teste que roda contra versao diferente da que vai
    // para producao nao prova o que promete.
    private readonly MsSqlContainer banco =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string StringDeConexao => banco.GetConnectionString();

    /// <summary>
    /// Pasta dos documentos, propria desta execucao. Sai junto no fim: teste que deixa
    /// arquivo para tras faz o proximo passar por motivo errado.
    /// </summary>
    public string PastaDeDocumentos { get; } =
        Path.Combine(Path.GetTempPath(), $"banco-documentos-{Guid.NewGuid():N}");

    /// <summary>
    /// Relogio da API. Comeca na hora de verdade; o teste que precisa de tempo passando
    /// avanca e reinicia depois, ja que o xUnit roda a colecao em sequencia.
    /// </summary>
    public RelogioControlado Relogio { get; } = new();

    /// <summary>
    /// O extrator que a API usa nos testes: o de ensaio de verdade, com uma chave para
    /// forcar falha.
    /// </summary>
    /// <remarks>
    /// Embrulha o real em vez de substituir por um dubio: assim o caminho feliz exercita o
    /// extrator que vai para producao quando nao ha provedor configurado, e so o caminho de
    /// falha e simulado — porque nao existe arquivo que derrube o extrator sob encomenda.
    /// </remarks>
    public ExtratorControlado Extrator { get; } = new();

    public async Task InitializeAsync()
    {
        await banco.StartAsync();

        using var escopo = Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<ContextoDoBanco>().Database.MigrateAsync();
    }

    // WebApplicationFactory ja expoe DisposeAsync devolvendo ValueTask e o xUnit exige
    // Task. Implementacao explicita e o que faz as duas assinaturas conviverem.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await banco.DisposeAsync();

        try
        {
            Directory.Delete(PastaDeDocumentos, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nenhum teste de documento rodou nesta execucao. Nada a limpar.
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AplicarConfiguracaoDeTeste(StringDeConexao, PastaDeDocumentos);
        builder.ConfigureTestServices(servicos =>
        {
            servicos.AddSingleton<TimeProvider>(Relogio);
            servicos.AddSingleton<IExtratorDeDocumento>(Extrator);

            // O pipeline nao roda na API em producao; quem o hospeda e o worker. Registrado
            // aqui para o teste poder dar uma rodada por chamada, em vez de subir um
            // processo e esperar pelo laco.
            servicos.AddScoped<ExtrairProximoDocumento>();
        });
    }
}

internal static class ConfiguracaoDeTeste
{
    // Entra por ultimo para vencer o .env que o Program carrega na subida.
    public static void AplicarConfiguracaoDeTeste(
        this IWebHostBuilder builder,
        string stringDeConexao,
        string pastaDeDocumentos) =>
        builder.ConfigureAppConfiguration(configuracao => configuracao.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Banco"] = stringDeConexao,
                ["Documentos:Raiz"] = pastaDeDocumentos,
            }));
}

/// <summary>O extrator de ensaio com uma chave de falha.</summary>
public sealed class ExtratorControlado : IExtratorDeDocumento
{
    private readonly ExtratorDeEnsaio real = new();

    /// <summary>Quando preenchido, toda extracao falha com esta mensagem.</summary>
    public string? MensagemDeFalha { get; set; }

    public int Chamadas { get; private set; }

    public void Reiniciar()
    {
        MensagemDeFalha = null;
        Chamadas = 0;
    }

    public Task<TextoDoDocumento> Extrair(
        Stream conteudo,
        TipoDeArquivo tipo,
        CancellationToken cancelamento)
    {
        Chamadas++;

        return MensagemDeFalha is { } mensagem
            ? throw new InvalidDataException(mensagem)
            : real.Extrair(conteudo, tipo, cancelamento);
    }
}

/// <summary>Relogio real com um deslocamento que o teste controla.</summary>
public sealed class RelogioControlado : TimeProvider
{
    private TimeSpan deslocamento = TimeSpan.Zero;

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + deslocamento;

    public void Avancar(TimeSpan quanto) => deslocamento += quanto;

    public void Reiniciar() => deslocamento = TimeSpan.Zero;
}

/// <summary>
/// Um container de SQL Server para a suite inteira. Por classe, cada uma pagaria a subida
/// do banco de novo — mais de meio minuto cada.
/// </summary>
[CollectionDefinition(Nome)]
public sealed class ColecaoDaApi : ICollectionFixture<FabricaDaApi>
{
    public const string Nome = "api";
}

internal static class Pedidos
{
    public static readonly System.Text.Json.JsonSerializerOptions Json =
        new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

    public static string ChaveNova() => Guid.NewGuid().ToString();

    public static string Agora(FabricaDaApi fabrica) =>
        fabrica.Relogio.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
}
