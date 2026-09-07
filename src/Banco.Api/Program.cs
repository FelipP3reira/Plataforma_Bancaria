using System.Globalization;
using System.Text.Json.Serialization;
using Banco.Api.Configuracao;
using Banco.Api.Contas;
using Serilog;

// Em desenvolvimento os segredos vem do .env; em producao, das variaveis de ambiente do
// proprio host. Os nomes com duplo sublinhado ja caem na configuracao sem mapeamento.
DotNetEnv.Env.TraversePath().Load();

var construtor = WebApplication.CreateBuilder(args);

construtor.Services.AddSerilog((servicos, registro) => registro
    .ReadFrom.Configuration(construtor.Configuration)
    .ReadFrom.Services(servicos)
    .Enrich.FromLogContext()

    // Cultura fixa no log: com pt-BR, valor decimal sairia com virgula e quebraria
    // qualquer coisa que leia esses campos depois.
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

construtor.Services.ConfigureHttpJsonOptions(opcoes =>
    opcoes.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

construtor.Services.AdicionarBanco();

var aplicacao = construtor.Build();

// O registro de requisicao fica por fora do tratador de erros de proposito: assim ele
// enxerga o status final. Por dentro, uma conta inexistente apareceria no log como 500 com
// pilha inteira, e nao como o 404 que o cliente de fato recebeu.
aplicacao.UseSerilogRequestLogging();

aplicacao.UseExceptionHandler();

if (!aplicacao.Environment.IsDevelopment())
{
    aplicacao.UseHsts();
    aplicacao.UseHttpsRedirection();
}

aplicacao.Use(async (contexto, proximo) =>
{
    // API que so devolve JSON: sem isso um navegador ainda pode ser induzido a interpretar
    // a resposta como outra coisa.
    contexto.Response.Headers["X-Content-Type-Options"] = "nosniff";
    contexto.Response.Headers["Referrer-Policy"] = "no-referrer";
    contexto.Response.Headers["X-Frame-Options"] = "DENY";

    await proximo().ConfigureAwait(false);
});

aplicacao.MapearContas();

await aplicacao.RunAsync().ConfigureAwait(false);

// Visivel para a fabrica de aplicacao dos testes de integracao.
public partial class Program;
