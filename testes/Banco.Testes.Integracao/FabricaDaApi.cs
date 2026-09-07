using System.Globalization;
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
    /// Relogio da API. Comeca na hora de verdade; o teste que precisa de tempo passando
    /// avanca e reinicia depois, ja que o xUnit roda a colecao em sequencia.
    /// </summary>
    public RelogioControlado Relogio { get; } = new();

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
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AplicarConfiguracaoDeTeste(StringDeConexao);
        builder.ConfigureTestServices(servicos => servicos.AddSingleton<TimeProvider>(Relogio));
    }
}

internal static class ConfiguracaoDeTeste
{
    // Entra por ultimo para vencer o .env que o Program carrega na subida.
    public static void AplicarConfiguracaoDeTeste(this IWebHostBuilder builder, string stringDeConexao) =>
        builder.ConfigureAppConfiguration(configuracao => configuracao.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Banco"] = stringDeConexao,
            }));
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
