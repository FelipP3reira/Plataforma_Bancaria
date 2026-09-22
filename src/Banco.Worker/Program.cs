using System.Globalization;
using Banco.Aplicacao.Documentos;
using Banco.Infraestrutura.Configuracao;
using Banco.Worker;
using Serilog;

// Mesmo .env da API: os dois processos falam com o mesmo banco e leem a mesma pasta de
// documentos, e duas fontes de configuracao seriam duas chances de divergir.
DotNetEnv.Env.TraversePath().Load();

var construtor = Host.CreateApplicationBuilder(args);

construtor.Services.AddSerilog((servicos, registro) => registro
    .ReadFrom.Configuration(construtor.Configuration)
    .ReadFrom.Services(servicos)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

construtor.Services.AdicionarInfraestrutura(construtor.Configuration);
construtor.Services.AddScoped<ExtrairProximoDocumento>();

construtor.Services.AddHostedService<ServicoDeExtracao>();

// O worker nao aplica migracao. Ele consome uma tabela que ja existe, e migracao disparada
// por processo que escala horizontalmente e disputa pela tabela de historico do EF.
await construtor.Build().RunAsync().ConfigureAwait(false);
