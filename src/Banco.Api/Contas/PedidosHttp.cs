using Banco.Dominio.Comum;
using Banco.Dominio.Contas;
using Banco.Dominio.Ledger;
using FluentValidation;

namespace Banco.Api.Contas;

public sealed record PedidoDeAberturaHttp(string Titular);

public sealed record MovimentacaoHttp(decimal Valor, string Descricao);

/// <param name="Efeito">
/// Quanto o lancamento moveu o saldo, com sinal. Vai junto do tipo porque quem consome
/// costuma querer somar uma lista, e somar exige o sinal que o campo Valor nao tem.
/// </param>
public sealed record RespostaDeLancamento(
    Guid Id,
    Guid ContaId,
    long Sequencia,
    TipoDeLancamento Tipo,
    decimal Valor,
    decimal Efeito,
    decimal SaldoDepois,
    string Descricao,
    string Origem,
    DateTimeOffset CriadoEm)
{
    public static RespostaDeLancamento De(Lancamento lancamento)
    {
        ArgumentNullException.ThrowIfNull(lancamento);

        return new RespostaDeLancamento(
            lancamento.Id,
            lancamento.ContaId,
            lancamento.Sequencia,
            lancamento.Tipo,
            lancamento.Valor.Valor,
            lancamento.Efeito,
            lancamento.SaldoDepois.Valor,
            lancamento.Descricao,
            lancamento.Origem,
            lancamento.CriadoEm);
    }
}

internal sealed class ValidadorDeAbertura : AbstractValidator<PedidoDeAberturaHttp>
{
    public ValidadorDeAbertura() =>
        RuleFor(pedido => pedido.Titular)
            .NotEmpty().WithMessage("Informe o titular da conta.")
            .MaximumLength(Conta.TamanhoMaximoDoTitular);
}

/// <remarks>
/// Repete o que o dominio ja garante, de proposito. Aqui o objetivo e dizer ao cliente qual
/// campo esta errado e por que, num formato que ele consegue mostrar na tela; no dominio e
/// impedir que o lancamento exista, venha de onde vier.
/// </remarks>
internal sealed class ValidadorDeMovimentacao : AbstractValidator<MovimentacaoHttp>
{
    public ValidadorDeMovimentacao()
    {
        RuleFor(pedido => pedido.Valor)
            .GreaterThan(0m).WithMessage("O valor precisa ser maior que zero.")
            .Must(valor => decimal.Round(valor, Dinheiro.CasasDecimais) == valor)
            .WithMessage($"O valor nao pode ter mais de {Dinheiro.CasasDecimais} casas decimais.");

        RuleFor(pedido => pedido.Descricao)
            .NotEmpty().WithMessage("Descreva a movimentacao.")
            .MaximumLength(Lancamento.TamanhoMaximoDaDescricao);
    }
}
