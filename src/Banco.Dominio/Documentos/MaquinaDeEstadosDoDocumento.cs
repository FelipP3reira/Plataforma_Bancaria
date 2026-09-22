using System.Collections.Frozen;
using Banco.Dominio.Erros;

namespace Banco.Dominio.Documentos;

/// <summary>
/// Valores fixados explicitamente: o estado e persistido como inteiro e reordenar o enum
/// nao pode reescrever o que ja foi gravado.
/// </summary>
public enum EstadoDoDocumento
{
    Recebido = 1,
    Extraindo = 2,
    Extraido = 3,
    RequerRevisao = 4,
    Revisado = 5,
    Pago = 6,
    Falhou = 7,
}

/// <summary>
/// O caminho que um documento percorre. A ausencia de uma aresta e proibicao.
/// </summary>
/// <remarks>
/// Tabela explicita pelo mesmo motivo da maquina de estados da conta: o conjunto inteiro do
/// que e permitido cabe em sete linhas e da para conferir de olho. Com condicionais
/// espalhadas pelo pipeline, "documento pago pode voltar para revisao?" viraria pergunta
/// que so o depurador responde — e a resposta errada aqui e dinheiro saindo duas vezes.
/// </remarks>
public static class MaquinaDeEstadosDoDocumento
{
    private static readonly FrozenDictionary<EstadoDoDocumento, EstadoDoDocumento[]> Permitidas =
        new Dictionary<EstadoDoDocumento, EstadoDoDocumento[]>
        {
            [EstadoDoDocumento.Recebido] = [EstadoDoDocumento.Extraindo],

            // Voltar para Recebido e o caminho do lease vencido: o worker morreu no meio e
            // outro precisa poder pegar. Sem essa aresta, documento de worker derrubado
            // ficaria presto em Extraindo para sempre.
            [EstadoDoDocumento.Extraindo] =
            [
                EstadoDoDocumento.Extraido,
                EstadoDoDocumento.RequerRevisao,
                EstadoDoDocumento.Falhou,
                EstadoDoDocumento.Recebido,
            ],

            // Extraido com confianca alta ainda pode cair na revisao: quem confere na tela
            // pode discordar do modelo, e discordar tem que ser possivel.
            [EstadoDoDocumento.Extraido] = [EstadoDoDocumento.RequerRevisao, EstadoDoDocumento.Pago],

            [EstadoDoDocumento.RequerRevisao] = [EstadoDoDocumento.Revisado],
            [EstadoDoDocumento.Revisado] = [EstadoDoDocumento.Pago],

            // Pago e terminal. O dinheiro ja saiu da conta, e o extrato e a fonte da
            // verdade do que aconteceu: reabrir o documento nao desfaz o lancamento.
            [EstadoDoDocumento.Pago] = [],

            // Falhou volta para a fila por decisao de gente, nao sozinho — o que falhou N
            // vezes sozinho falharia a N+1 tambem.
            [EstadoDoDocumento.Falhou] = [EstadoDoDocumento.Recebido],
        }.ToFrozenDictionary();

    public static bool Aceita(EstadoDoDocumento de, EstadoDoDocumento para) =>
        Permitidas.TryGetValue(de, out var destinos) && Array.IndexOf(destinos, para) >= 0;

    public static void Garantir(EstadoDoDocumento de, EstadoDoDocumento para)
    {
        if (!Aceita(de, para))
        {
            throw new TransicaoInvalidaException($"Documento {de} nao pode passar para {para}.");
        }
    }
}
