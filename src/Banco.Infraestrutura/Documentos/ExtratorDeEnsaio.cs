using System.Text;
using Banco.Aplicacao.Portas;
using Banco.Dominio.Documentos;

namespace Banco.Infraestrutura.Documentos;

/// <summary>
/// Extrator que nao chama servico nenhum: le os trechos de texto legivel do proprio arquivo.
/// </summary>
/// <remarks>
/// Existe para o pipeline poder rodar inteiro sem provedor de IA configurado — em
/// desenvolvimento, na suite de testes e na primeira subida de quem clonou o repositorio.
/// <para>
/// Nao e um extrator de verdade e nao finge ser: PDF com texto comprimido nao devolve nada
/// legivel aqui. O que ele garante e ser <em>deterministico</em> — o mesmo arquivo sempre da
/// o mesmo resultado, que e o que um teste de fila precisa. A leitura de verdade entra pelo
/// provedor configuravel, atras da mesma porta.
/// </para>
/// </remarks>
public sealed class ExtratorDeEnsaio : IExtratorDeDocumento
{
    /// <summary>Tamanho minimo de um trecho para valer como texto, e nao como ruido.</summary>
    private const int MinimoDeCaracteres = 4;

    /// <summary>
    /// Quanto do arquivo o extrator olha.
    /// </summary>
    /// <remarks>
    /// Teto e nao o arquivo inteiro: dez megabytes de PDF virariam dez megabytes de coluna
    /// no banco, e nenhum boleto precisa disso para ter seus campos lidos.
    /// </remarks>
    private const int LimiteDeBytes = 64 * 1024;

    public async Task<TextoDoDocumento> Extrair(
        Stream conteudo,
        TipoDeArquivo tipo,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(conteudo);

        var bytes = new byte[LimiteDeBytes];
        var lidos = await conteudo.ReadAtLeastAsync(bytes, LimiteDeBytes, throwOnEndOfStream: false, cancelamento)
            .ConfigureAwait(false);

        var trechos = TrechosLegiveis(bytes.AsSpan(0, lidos));
        var texto = string.Join('\n', trechos);

        return new TextoDoDocumento(texto, Confianca(texto.Length, lidos));
    }

    private static List<string> TrechosLegiveis(ReadOnlySpan<byte> bytes)
    {
        var trechos = new List<string>();
        var atual = new StringBuilder();

        foreach (var valor in bytes)
        {
            // Faixa ASCII imprimivel. Byte fora dela encerra o trecho corrente: e dado
            // binario, e emendar os dois lados dele juntaria palavras que nao se tocam.
            if (valor is >= 0x20 and <= 0x7E)
            {
                atual.Append((char)valor);
                continue;
            }

            Fechar(trechos, atual);
        }

        Fechar(trechos, atual);

        return trechos;
    }

    private static void Fechar(List<string> trechos, StringBuilder atual)
    {
        if (atual.Length >= MinimoDeCaracteres)
        {
            trechos.Add(atual.ToString());
        }

        atual.Clear();
    }

    /// <summary>
    /// Quanto do arquivo saiu como texto legivel, de 0 a 1.
    /// </summary>
    /// <remarks>
    /// Proporcao, e nao valor fixo: e a medida honesta do que este extrator conseguiu ler, e
    /// e ela que faz PDF comprimido cair na revisao manual em vez de passar como extraido.
    /// </remarks>
    private static decimal Confianca(int caracteres, int bytesLidos) =>
        bytesLidos == 0 ? 0m : Math.Round(Math.Min(1m, (decimal)caracteres / bytesLidos), 4);
}
