using Banco.Api.Contas;
using Banco.Api.Erros;
using Banco.Aplicacao.Conciliacao;
using Banco.Aplicacao.Contas;
using Banco.Aplicacao.Portas;
using Banco.Infraestrutura.Persistencia;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Banco.Api.Configuracao;

internal static class ServicosDoBanco
{
    public static IServiceCollection AdicionarBanco(this IServiceCollection servicos)
    {
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
        servicos.AddScoped<IUnidadeDeTrabalho, UnidadeDeTrabalho>();

        servicos.AddScoped<AbrirConta>();
        servicos.AddScoped<ConsultarConta>();
        servicos.AddScoped<MovimentarConta>();
        servicos.AddScoped<ConciliarConta>();

        servicos.AddScoped<IValidator<PedidoDeAberturaHttp>, ValidadorDeAbertura>();
        servicos.AddScoped<IValidator<MovimentacaoHttp>, ValidadorDeMovimentacao>();

        servicos.AddProblemDetails();
        servicos.AddExceptionHandler<TratamentoDeErrosDeDominio>();

        return servicos;
    }
}
