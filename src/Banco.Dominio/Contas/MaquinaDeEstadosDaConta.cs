using System.Collections.Frozen;
using Banco.Dominio.Erros;

namespace Banco.Dominio.Contas;

/// <summary>
/// Valores fixados explicitamente: o estado e persistido como inteiro e reordenar o enum
/// nao pode reescrever o historico ja gravado.
/// </summary>
public enum EstadoDaConta
{
    Ativa = 1,
    Bloqueada = 2,
    Encerrada = 3,
}

/// <summary>
/// As transicoes que a conta aceita. A ausencia de uma aresta e proibicao, nao omissao.
/// </summary>
/// <remarks>
/// Tabela explicita em vez de uma sequencia de <c>if</c>: o conjunto inteiro do que e
/// permitido cabe em cinco linhas e da para conferir de olho. Com condicionais espalhadas
/// pelos metodos, "a conta encerrada pode voltar?" viraria uma pergunta que so o depurador
/// responde.
/// </remarks>
public static class MaquinaDeEstadosDaConta
{
    private static readonly FrozenDictionary<EstadoDaConta, EstadoDaConta[]> Permitidas =
        new Dictionary<EstadoDaConta, EstadoDaConta[]>
        {
            [EstadoDaConta.Ativa] = [EstadoDaConta.Bloqueada, EstadoDaConta.Encerrada],
            [EstadoDaConta.Bloqueada] = [EstadoDaConta.Ativa, EstadoDaConta.Encerrada],

            // Encerrada e terminal. Reabrir seria apagar o motivo do encerramento; quem
            // quiser voltar a ter conta abre outra, e as duas historias ficam separadas.
            [EstadoDaConta.Encerrada] = [],
        }.ToFrozenDictionary();

    public static bool Aceita(EstadoDaConta de, EstadoDaConta para) =>
        Permitidas.TryGetValue(de, out var destinos) && Array.IndexOf(destinos, para) >= 0;

    public static void Garantir(EstadoDaConta de, EstadoDaConta para)
    {
        if (!Aceita(de, para))
        {
            throw new TransicaoInvalidaException($"Conta {de} nao pode passar para {para}.");
        }
    }

    /// <summary>Só conta ativa movimenta dinheiro.</summary>
    public static bool Movimenta(EstadoDaConta estado) => estado == EstadoDaConta.Ativa;
}
