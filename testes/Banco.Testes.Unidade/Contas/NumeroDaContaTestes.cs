using Banco.Dominio.Contas;
using Banco.Dominio.Erros;

namespace Banco.Testes.Unidade.Contas;

public class NumeroDaContaTestes
{
    [Fact]
    public void MontaComSeteDigitosEHifen()
    {
        var numero = NumeroDaConta.DaSequencia(42);

        Assert.StartsWith("0000042-", numero.Texto, StringComparison.Ordinal);
        Assert.Equal(9, numero.Texto.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000_000)]
    public void RecusaSequenciaForaDaFaixa(long sequencia) =>
        Assert.Throws<ContaInvalidaException>(() => NumeroDaConta.DaSequencia(sequencia));

    [Fact]
    public void OQueEleGeraEleAceitaDeVolta()
    {
        // Varre a faixa inteira em passos irregulares: o digito e calculado, entao o que
        // interessa e nenhum resto do modulo 11 escapar da regra de arredondamento.
        for (long sequencia = 1; sequencia < 9_999_999; sequencia += 7919)
        {
            var texto = NumeroDaConta.DaSequencia(sequencia).Texto;

            Assert.True(NumeroDaConta.TentarCriar(texto, out var lido), texto);
            Assert.Equal(texto, lido.Texto);
        }
    }

    [Fact]
    public void AceitaSemOHifenEDevolveComEle()
    {
        var comHifen = NumeroDaConta.DaSequencia(123456).Texto;
        var semHifen = comHifen.Replace("-", string.Empty, StringComparison.Ordinal);

        Assert.Equal(comHifen, NumeroDaConta.Criar(semHifen).Texto);
    }

    /// <summary>
    /// O ponto do verificador: um digito trocado precisa virar "conta invalida" antes de
    /// virar consulta que acha a conta de outra pessoa.
    /// </summary>
    [Fact]
    public void UmDigitoTrocadoNaoPassa()
    {
        var original = NumeroDaConta.DaSequencia(7654321).Texto;
        var recusados = 0;
        var tentativas = 0;

        for (var posicao = 0; posicao < NumeroDaConta.DigitosDaSequencia; posicao++)
        {
            foreach (var digito in "0123456789")
            {
                if (original[posicao] == digito)
                {
                    continue;
                }

                var adulterado = original.ToCharArray();
                adulterado[posicao] = digito;
                tentativas++;

                if (!NumeroDaConta.TentarCriar(new string(adulterado), out _))
                {
                    recusados++;
                }
            }
        }

        Assert.Equal(tentativas, recusados);
    }

    // Transposicao e o segundo erro de digitacao mais comum, e so peso crescente a pega.
    [Fact]
    public void DoisDigitosVizinhosTrocadosDeLugarNaoPassam()
    {
        var original = NumeroDaConta.DaSequencia(1234567).Texto;

        for (var posicao = 0; posicao < NumeroDaConta.DigitosDaSequencia - 1; posicao++)
        {
            if (original[posicao] == original[posicao + 1])
            {
                continue;
            }

            var trocado = original.ToCharArray();
            (trocado[posicao], trocado[posicao + 1]) = (trocado[posicao + 1], trocado[posicao]);

            Assert.False(NumeroDaConta.TentarCriar(new string(trocado), out _), new string(trocado));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("12345678901")]
    [InlineData("abcdefg-h")]
    public void EntradaEstragadaEhRecusadaSemEstourar(string? entrada) =>
        Assert.False(NumeroDaConta.TentarCriar(entrada, out _));

    [Fact]
    public void CriarComEntradaInvalidaEstoura() =>
        Assert.Throws<ContaInvalidaException>(() => NumeroDaConta.Criar("0000001-0"));
}
