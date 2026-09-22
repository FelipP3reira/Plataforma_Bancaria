using System.Text;
using Banco.Dominio.Documentos;
using Banco.Dominio.Documentos.Boletos;
using Banco.Dominio.Erros;
using Banco.Dominio.Ledger;

namespace Banco.Testes.Unidade.Documentos;

/// <summary>
/// O encaminhamento por confianca, a correcao humana e o fechamento em pago.
/// </summary>
public class RevisaoDoDocumentoTestes
{
    private const string LinhaValida = "00191234546789012345767890123457915950000018990";

    private static readonly DateTimeOffset Agora = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

    private static Documento Extraido(decimal confianca, decimal limiar, params CampoLido[] campos)
    {
        var documento = Documento.Receber(
            Guid.CreateVersion7(),
            "boleto.pdf",
            TipoDeArquivo.Pdf,
            1024,
            HashDoArquivo.De(Encoding.UTF8.GetBytes("boleto")),
            "ab/abc.pdf",
            "web:Ana Ribeiro",
            Agora);

        documento.Reservar(Agora, TimeSpan.FromMinutes(2));
        documento.Concluir("texto", 1m, new LeituraDoDocumento(campos, confianca), limiar, Agora);

        return documento;
    }

    private static CampoLido Campo(NomeDoCampo nome, string valor, decimal confianca = 1m) =>
        new(nome, valor, confianca, OrigemDoCampo.Estrutura);

    private static Documento Completo(decimal confianca = 1m, decimal limiar = 0.80m) =>
        Extraido(
            confianca,
            limiar,
            Campo(NomeDoCampo.LinhaDigitavel, LinhaValida),
            Campo(NomeDoCampo.Valor, "189.90"),
            Campo(NomeDoCampo.Vencimento, "2026-10-10"));

    [Fact]
    public void ConfiancaAcimaDoLimiarSegueSozinha() =>
        Assert.Equal(EstadoDoDocumento.Extraido, Completo(confianca: 0.90m).Estado);

    [Fact]
    public void ConfiancaAbaixoDoLimiarParaParaRevisao() =>
        Assert.Equal(EstadoDoDocumento.RequerRevisao, Completo(confianca: 0.20m).Estado);

    /// <summary>
    /// Exatamente no limiar passa. Com <c>&lt;=</c>, o limiar em 1,00 mandaria para a revisao
    /// tambem o documento que se confere inteiro — e nao existe leitura melhor do que essa.
    /// </summary>
    [Fact]
    public void ConfiancaExatamenteNoLimiarPassa() =>
        Assert.Equal(EstadoDoDocumento.Extraido, Completo(confianca: 0.80m, limiar: 0.80m).Estado);

    /// <summary>
    /// O encaminhamento acontece no mesmo metodo que grava. Em dois passos existiria um instante
    /// com o documento extraido, confianca baixa e ninguem marcando revisao — e um processo que
    /// caisse ali deixaria o documento parecendo aprovado.
    /// </summary>
    [Fact]
    public void OEncaminhamentoEAGravacaoAcontecemJuntos()
    {
        var documento = Completo(confianca: 0.20m);

        Assert.Equal(EstadoDoDocumento.RequerRevisao, documento.Estado);
        Assert.Equal(0.20m, documento.Confianca);
        Assert.NotNull(documento.ExtraidoEm);
    }

    [Fact]
    public void RevisarSemCorrecaoFechaComoConferido()
    {
        var documento = Completo(confianca: 0.20m);
        documento.Revisar(new Dictionary<NomeDoCampo, string>(), "web:Bruno", Agora);

        Assert.Equal(EstadoDoDocumento.Revisado, documento.Estado);
        Assert.All(documento.Campos, campo => Assert.False(campo.FoiCorrigido));
    }

    /// <summary>
    /// A correcao entra ao lado do valor lido, e nao no lugar dele: e o unico registro do que a
    /// maquina errou, e e ele que diz depois onde vale a pena melhorar o extrator.
    /// </summary>
    [Fact]
    public void ACorrecaoNaoApagaOQueAMaquinaLeu()
    {
        var documento = Completo(confianca: 0.20m);
        documento.Revisar(
            new Dictionary<NomeDoCampo, string> { [NomeDoCampo.Valor] = "1.402,77" },
            "web:Bruno",
            Agora);

        var valor = documento.Campo(NomeDoCampo.Valor);

        Assert.Equal("189.90", valor.ValorLido);
        Assert.Equal("1402.77", valor.ValorCorrigido);
        Assert.Equal("1402.77", valor.ValorFinal);
        Assert.Equal("web:Bruno", valor.CorrigidoPor);
        Assert.Equal(Agora, valor.CorrigidoEm);
    }

    [Fact]
    public void CampoNaoCorrigidoContinuaValendoOQueFoiLido() =>
        Assert.Equal("2026-10-10", Revisado().Campo(NomeDoCampo.Vencimento).ValorFinal);

    /// <summary>
    /// A correcao e a unica porta por onde um valor entra sem ter passado pelo verificador da
    /// leitura automatica. Sem conferir aqui, corrigir a linha digitavel para qualquer coisa
    /// fecharia o documento e o pagamento cobraria o que foi digitado.
    /// </summary>
    [Theory]
    [InlineData("12345")]
    [InlineData("00191234546789012345767890123457915950000018991")]
    [InlineData("")]
    public void LinhaDigitavelCorrigidaParaAlgoInvalidoEhRecusada(string valor) =>
        Assert.Throws<BoletoInvalidoException>(
            () => Completo(confianca: 0.20m).Revisar(
                new Dictionary<NomeDoCampo, string> { [NomeDoCampo.LinhaDigitavel] = valor },
                "web:Bruno",
                Agora));

    [Theory]
    [InlineData("nao e valor")]
    [InlineData("-50,00")]
    [InlineData("10,999")]
    public void ValorCorrigidoParaAlgoInvalidoEhRecusado(string valor) =>
        Assert.ThrowsAny<DominioException>(
            () => Completo(confianca: 0.20m).Revisar(
                new Dictionary<NomeDoCampo, string> { [NomeDoCampo.Valor] = valor },
                "web:Bruno",
                Agora));

    // Quem corrige na tela digita a forma brasileira; recusar por causa da virgula seria rigor
    // contra a pessoa errada.
    [Theory]
    [InlineData("1.402,77", "1402.77")]
    [InlineData("1402.77", "1402.77")]
    [InlineData("80,00", "80.00")]
    public void OValorCorrigidoEhCanonizado(string digitado, string esperado)
    {
        var documento = Completo(confianca: 0.20m);
        documento.Revisar(
            new Dictionary<NomeDoCampo, string> { [NomeDoCampo.Valor] = digitado },
            "web:Bruno",
            Agora);

        Assert.Equal(esperado, documento.Campo(NomeDoCampo.Valor).ValorFinal);
    }

    [Theory]
    [InlineData("10/10/2026", "2026-10-10")]
    [InlineData("2026-10-10", "2026-10-10")]
    public void ADataCorrigidaAceitaAsDuasFormas(string digitado, string esperado)
    {
        var documento = Completo(confianca: 0.20m);
        documento.Revisar(
            new Dictionary<NomeDoCampo, string> { [NomeDoCampo.Vencimento] = digitado },
            "web:Bruno",
            Agora);

        Assert.Equal(esperado, documento.Campo(NomeDoCampo.Vencimento).ValorFinal);
    }

    /// <summary>
    /// Corrigir campo que a leitura nao produziu nao e corrigir: e inventar um campo sem nada
    /// dizendo de onde ele veio. O caminho certo nesse caso e o reprocessamento.
    /// </summary>
    [Fact]
    public void NaoDaParaCorrigirCampoQueAMaquinaNaoAchou()
    {
        var documento = Extraido(0.20m, 0.80m, Campo(NomeDoCampo.Valor, "80.00"));

        Assert.Throws<BoletoInvalidoException>(
            () => documento.Revisar(
                new Dictionary<NomeDoCampo, string> { [NomeDoCampo.CnpjDoEmissor] = "11222333000181" },
                "web:Bruno",
                Agora));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RevisaoSemRevisorEhRecusada(string revisor) =>
        Assert.Throws<ArquivoRecusadoException>(
            () => Completo(confianca: 0.20m).Revisar(new Dictionary<NomeDoCampo, string>(), revisor, Agora));

    /// <summary>
    /// Documento que passou sozinho tambem pode ser revisado: quem confere na tela pode discordar
    /// do modelo mesmo com confianca alta, e discordar tem que ser possivel.
    /// </summary>
    [Fact]
    public void DocumentoQuePassouSozinhoAindaPodeSerConferido()
    {
        var documento = Completo(confianca: 1m);
        documento.Revisar(new Dictionary<NomeDoCampo, string>(), "web:Bruno", Agora);

        Assert.Equal(EstadoDoDocumento.Revisado, documento.Estado);
    }

    [Fact]
    public void RevisarDuasVezesEhRecusado()
    {
        var documento = Revisado();

        Assert.Throws<TransicaoInvalidaException>(
            () => documento.Revisar(new Dictionary<NomeDoCampo, string>(), "web:Bruno", Agora));
    }

    /// <summary>
    /// A chave de idempotencia do pagamento e derivada do documento, e nao sorteada: repetir um
    /// pagamento interrompido apresenta a mesma chave e a conta reconhece o debito que ja existe.
    /// </summary>
    [Fact]
    public void AChaveDePagamentoNaoMuda()
    {
        var documento = Completo();

        Assert.Equal(documento.ChaveDePagamento(), documento.ChaveDePagamento());
        Assert.Contains(documento.Id.ToString("N"), documento.ChaveDePagamento(), StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentosDiferentesTemChavesDePagamentoDiferentes() =>
        Assert.NotEqual(Completo().ChaveDePagamento(), Completo().ChaveDePagamento());

    /// <summary>
    /// A chave precisa caber no teto do ledger. Um SHA-256 com prefixo passaria de 64 caracteres, e
    /// o pagamento morreria na validacao da movimentacao em vez de na compilacao.
    /// </summary>
    [Fact]
    public void AChaveDePagamentoCabeNoLimiteDoLedger() =>
        Assert.InRange(Completo().ChaveDePagamento().Length, 1, PedidoDeLancamento.TamanhoMaximoDaChave);

    [Fact]
    public void PagarFechaODocumentoContraOLancamento()
    {
        var lancamento = Guid.CreateVersion7();
        var documento = Revisado();
        documento.Pagar(lancamento, Agora);

        Assert.Equal(EstadoDoDocumento.Pago, documento.Estado);
        Assert.Equal(lancamento, documento.LancamentoDoPagamentoId);
        Assert.Equal(Agora, documento.PagoEm);
    }

    [Fact]
    public void DocumentoExtraidoPodeSerPagoSemPassarPelaRevisao()
    {
        var documento = Completo(confianca: 1m);
        documento.Pagar(Guid.CreateVersion7(), Agora);

        Assert.Equal(EstadoDoDocumento.Pago, documento.Estado);
    }

    /// <summary>
    /// Documento esperando olho humano nao se paga. E o ponto da fila: a confianca baixa segura o
    /// dinheiro ate alguem confirmar.
    /// </summary>
    [Fact]
    public void DocumentoEsperandoRevisaoNaoPodeSerPago() =>
        Assert.Throws<TransicaoInvalidaException>(
            () => Completo(confianca: 0.20m).Pagar(Guid.CreateVersion7(), Agora));

    /// <summary>
    /// A conferencia de "pode pagar?" tem que existir separada da transicao, porque o debito
    /// acontece entre as duas. Sem ela, documento esperando revisao era cobrado e so entao recusado
    /// — o dinheiro saia da conta e o documento continuava na fila.
    /// </summary>
    [Fact]
    public void AConferenciaDePagavelRecusaODocumentoEsperandoRevisao() =>
        Assert.Throws<TransicaoInvalidaException>(
            () => Completo(confianca: 0.20m).GarantirQuePodeSerPago());

    [Fact]
    public void AConferenciaDePagavelAceitaODocumentoExtraidoEORevisado()
    {
        Completo().GarantirQuePodeSerPago();
        Revisado().GarantirQuePodeSerPago();
    }

    [Fact]
    public void PagamentoSemLancamentoEhRecusado() =>
        Assert.Throws<ArquivoRecusadoException>(() => Revisado().Pagar(Guid.Empty, Agora));

    /// <summary>
    /// Pago e terminal. O dinheiro saiu e o extrato e a fonte da verdade: reabrir nao desfaz o
    /// lancamento, so abriria caminho para pagar de novo.
    /// </summary>
    [Fact]
    public void PagarDuasVezesEhRecusado()
    {
        var documento = Revisado();
        documento.Pagar(Guid.CreateVersion7(), Agora);

        Assert.Throws<TransicaoInvalidaException>(() => documento.Pagar(Guid.CreateVersion7(), Agora));
    }

    private static Documento Revisado()
    {
        var documento = Completo(confianca: 0.20m);
        documento.Revisar(
            new Dictionary<NomeDoCampo, string> { [NomeDoCampo.Valor] = "1.402,77" },
            "web:Bruno",
            Agora);

        return documento;
    }
}
