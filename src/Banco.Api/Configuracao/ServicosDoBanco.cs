using Banco.Api.Contas;
using Banco.Api.Erros;
using Banco.Api.Transferencias;
using Banco.Aplicacao.Conciliacao;
using Banco.Aplicacao.Contas;
using Banco.Aplicacao.Documentos;
using Banco.Aplicacao.Extrato;
using Banco.Aplicacao.Transferencias;
using Banco.Infraestrutura.Configuracao;
using FluentValidation;

namespace Banco.Api.Configuracao;

internal static class ServicosDoBanco
{
    public static IServiceCollection AdicionarBanco(this IServiceCollection servicos, IConfiguration configuracao)
    {
        servicos.AdicionarInfraestrutura(configuracao);

        servicos.AddScoped<AbrirConta>();
        servicos.AddScoped<ConsultarConta>();
        servicos.AddScoped<MovimentarConta>();
        servicos.AddScoped<MudarEstadoDaConta>();
        servicos.AddScoped<ConsultarHistoricoDeEstado>();
        servicos.AddScoped<ConciliarConta>();
        servicos.AddScoped<ConsultarExtrato>();
        servicos.AddScoped<ReceberDocumento>();
        servicos.AddScoped<ConsultarDocumento>();
        servicos.AddScoped<ReenfileirarDocumento>();
        servicos.AddScoped<TransferirEntreContas>();
        servicos.AddScoped<ConsultarTransferencia>();

        servicos.AddScoped<IValidator<PedidoDeAberturaHttp>, ValidadorDeAbertura>();
        servicos.AddScoped<IValidator<MovimentacaoHttp>, ValidadorDeMovimentacao>();
        servicos.AddScoped<IValidator<TransferenciaHttp>, ValidadorDeTransferencia>();
        servicos.AddScoped<IValidator<MudancaDeEstadoHttp>, ValidadorDeMudancaDeEstado>();

        servicos.AddProblemDetails();
        servicos.AddExceptionHandler<TratamentoDeErrosDeDominio>();

        return servicos;
    }
}
