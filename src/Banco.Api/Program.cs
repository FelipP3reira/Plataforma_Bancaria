using System.Globalization;
using System.Text.Json.Serialization;
using Banco.Api.Configuracao;
using Banco.Api.Contas;
using Banco.Api.Transferencias;
using Scalar.AspNetCore;
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

construtor.Services.AddOpenApi();
construtor.Services.AdicionarBanco();

var aplicacao = construtor.Build();

// O registro de requisicao fica por fora do tratador de erros de proposito: assim ele
// enxerga o status final. Por dentro, uma conta inexistente apareceria no log como 500 com
// pilha inteira, e nao como o 404 que o cliente de fato recebeu.
aplicacao.UseSerilogRequestLogging();

aplicacao.UseExceptionHandler();

// Documentacao navegavel so fora de producao. O contrato da API nao e segredo, mas uma
// interface que dispara requisicao de verdade nao precisa estar exposta no servidor que
// guarda dinheiro.
if (aplicacao.Environment.IsDevelopment())
{
    aplicacao.MapOpenApi();
    aplicacao.MapScalarApiReference("/docs", opcoes => opcoes.WithTitle("Plataforma Bancaria"));

    // A raiz existe so para nao devolver 404 a quem abre o endereco no navegador.
    aplicacao.MapGet("/", () => Results.Redirect("/docs")).ExcludeFromDescription();
}

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
aplicacao.MapearExtrato();
aplicacao.MapearEstadoDaConta();
aplicacao.MapearTransferencias();

await aplicacao.RunAsync().ConfigureAwait(false);

// Visivel para a fabrica de aplicacao dos testes de integracao.
public partial class Program;
