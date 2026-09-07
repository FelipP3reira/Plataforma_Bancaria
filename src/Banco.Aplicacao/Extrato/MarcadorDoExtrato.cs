using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Banco.Aplicacao.Erros;

namespace Banco.Aplicacao.Extrato;

/// <summary>
/// Onde a pagina anterior parou: o instante do ultimo lancamento devolvido e a posicao dele
/// no ledger.
/// </summary>
/// <remarks>
/// Paginacao por marcador, e nao por <c>OFFSET</c>. Extrato e uma lista que cresce por
/// cima: com <c>OFFSET</c>, um lancamento novo entre duas paginas empurra tudo para baixo e
/// o cliente ve a mesma linha duas vezes. O marcador ancora na linha, e nao na contagem, e
/// tambem evita que a pagina 500 custe ler as 499 anteriores.
/// <para>
/// Duas colunas porque uma nao basta. <see cref="CriadoEm"/> sozinho repete: o instante vem
/// do relogio no comeco do pedido, e dois lancamentos na mesma conta podem cair no mesmo
/// tick. <see cref="Sequencia"/> sozinha nao serve de marcador porque nao e ela que ordena
/// o extrato — a ordem e cronologica, e a sequencia so desempata. Juntas formam ordem
/// total: a sequencia e unica dentro da conta.
/// </para>
/// </remarks>
public readonly record struct MarcadorDoExtrato(DateTimeOffset CriadoEm, long Sequencia)
{
    private const char Separador = '|';

    /// <summary>
    /// Texto opaco, para o cliente devolver sem interpretar.
    /// </summary>
    /// <remarks>
    /// Base64Url, e nao Base64: o marcador viaja na query string, e <c>+</c> e <c>/</c>
    /// precisariam de escape que metade dos clientes esquece de fazer.
    /// </remarks>
    public string Codificar() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{CriadoEm:O}{Separador}{Sequencia}")));

    public static MarcadorDoExtrato Decodificar(string codificado)
    {
        if (string.IsNullOrWhiteSpace(codificado))
        {
            throw new ConsultaInvalidaException("Marcador de pagina vazio.");
        }

        // Marcador ilegivel e erro do pedido, e nao falha do servidor: quem digita a query
        // string a mao merece um 400 dizendo o que houve, e nao um 500.
        try
        {
            var texto = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(codificado));
            var partes = texto.Split(Separador);

            if (partes.Length != 2)
            {
                throw new ConsultaInvalidaException("Marcador de pagina mal formado.");
            }

            return new MarcadorDoExtrato(
                DateTimeOffset.Parse(partes[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                long.Parse(partes[1], CultureInfo.InvariantCulture));
        }
        catch (Exception erro) when (erro is FormatException or ArgumentException)
        {
            throw new ConsultaInvalidaException("Marcador de pagina mal formado.", erro);
        }
    }
}
