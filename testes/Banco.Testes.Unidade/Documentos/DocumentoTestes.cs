using System.Text;
using Banco.Dominio.Documentos;
using Banco.Dominio.Erros;

namespace Banco.Testes.Unidade.Documentos;

public class DocumentoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

    private static HashDoArquivo Hash(string conteudo = "boleto") =>
        HashDoArquivo.De(Encoding.UTF8.GetBytes(conteudo));

    private static Documento Receber(
        string nome = "boleto.pdf",
        long tamanho = 1024,
        string origem = "web:Ana Ribeiro") =>
        Documento.Receber(
            Guid.CreateVersion7(),
            nome,
            TipoDeArquivo.Pdf,
            tamanho,
            Hash(),
            "ab/abc.pdf",
            origem,
            Agora);

    [Fact]
    public void DocumentoNasceNaFila()
    {
        var documento = Receber();

        Assert.Equal(EstadoDoDocumento.Recebido, documento.Estado);
        Assert.Equal(Agora, documento.RecebidoEm);
        Assert.Equal(Agora, documento.AtualizadoEm);
    }

    /// <summary>
    /// O nome do arquivo é escolhido por quem envia, e <c>../</c> é um nome válido. Ele
    /// nunca entra no caminho de gravação, mas também não pode chegar inteiro ao banco.
    /// </summary>
    [Theory]
    [InlineData("../../appsettings.json", "appsettings.json")]
    [InlineData("..\\..\\web.config", "web.config")]
    [InlineData("/etc/passwd", "passwd")]
    [InlineData("C:\\Windows\\System32\\config\\SAM", "SAM")]
    public void CaminhoNoNomeDoArquivoEhDescartado(string enviado, string esperado) =>
        Assert.Equal(esperado, Receber(nome: enviado).NomeOriginal);

    /// <summary>
    /// Quebra de linha no nome vira duas linhas no log, e a segunda é escrita por quem
    /// enviou o arquivo.
    /// </summary>
    [Fact]
    public void CaractereDeControleNoNomeEhRemovido()
    {
        var documento = Receber(nome: "boleto\r\nINFO: pagamento aprovado.pdf");

        Assert.DoesNotContain('\r', documento.NomeOriginal);
        Assert.DoesNotContain('\n', documento.NomeOriginal);
    }

    [Fact]
    public void NomeLongoDemaisEhCortado() =>
        Assert.Equal(
            Documento.TamanhoMaximoDoNome,
            Receber(nome: new string('a', 500) + ".pdf").NomeOriginal.Length);

    // Nome que sobra vazio depois da limpeza ainda precisa dar um documento exibível.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    public void NomeVazioViraUmNomePadrao(string nome) =>
        Assert.Equal("documento.pdf", Receber(nome: nome).NomeOriginal);

    [Fact]
    public void ArquivoVazioEhRecusado() =>
        Assert.Throws<ArquivoRecusadoException>(() => Receber(tamanho: 0));

    [Fact]
    public void ArquivoAcimaDoLimiteEhRecusado() =>
        Assert.Throws<ArquivoRecusadoException>(
            () => Receber(tamanho: Documento.TamanhoMaximoEmBytes + 1));

    [Fact]
    public void ArquivoExatamenteNoLimitePassa() =>
        Assert.Equal(
            Documento.TamanhoMaximoEmBytes,
            Receber(tamanho: Documento.TamanhoMaximoEmBytes).TamanhoEmBytes);

    // Documento sem dono não se audita: não dá para dizer quem mandou pagar.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DocumentoSemOrigemEhRecusado(string origem) =>
        Assert.Throws<ArquivoRecusadoException>(() => Receber(origem: origem));

    [Fact]
    public void DocumentoSemCaminhoDeArquivoEhRecusado() =>
        Assert.Throws<ArquivoRecusadoException>(
            () => Documento.Receber(
                Guid.CreateVersion7(),
                "boleto.pdf",
                TipoDeArquivo.Pdf,
                1024,
                Hash(),
                caminhoRelativo: "  ",
                "web:Ana",
                Agora));
}

public class HashDoArquivoTestes
{
    private static byte[] Bytes(string texto) => Encoding.UTF8.GetBytes(texto);

    /// <summary>
    /// A propriedade que sustenta a idempotência: mesmo conteúdo, mesmo hash, sempre.
    /// </summary>
    [Fact]
    public void OMesmoConteudoDaSempreOMesmoHash() =>
        Assert.Equal(HashDoArquivo.De(Bytes("boleto")), HashDoArquivo.De(Bytes("boleto")));

    [Fact]
    public void ConteudosDiferentesDaoHashesDiferentes() =>
        Assert.NotEqual(HashDoArquivo.De(Bytes("boleto")), HashDoArquivo.De(Bytes("boletos")));

    [Fact]
    public void OHashTemSessentaEQuatroCaracteresHexadecimais()
    {
        var hash = HashDoArquivo.De(Bytes("boleto")).Texto;

        Assert.Equal(HashDoArquivo.TamanhoEmCaracteres, hash.Length);
        Assert.All(hash, caractere => Assert.True(char.IsAsciiHexDigitLower(caractere)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("ZZZZ2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b")]
    public void TextoQueNaoEhHashEhRecusado(string texto) =>
        Assert.Throws<ArquivoRecusadoException>(() => HashDoArquivo.DoTexto(texto));

    // Maiúscula é recusada de propósito: dois textos para o mesmo hash quebrariam o
    // índice único que sustenta a idempotência.
    [Fact]
    public void HashEmMaiusculaEhRecusado() =>
        Assert.Throws<ArquivoRecusadoException>(
            () => HashDoArquivo.DoTexto(HashDoArquivo.De(Bytes("boleto")).Texto.ToUpperInvariant()));
}

public class MaquinaDeEstadosDoDocumentoTestes
{
    /// <summary>
    /// A tabela inteira contra uma cópia escrita à parte, a partir do desenho do pipeline.
    /// Duplicar o dado é o objetivo: divergência entre as duas acusa erro de digitação.
    /// </summary>
    [Fact]
    public void ATabelaDeTransicoesBateComOPipelineDesenhado()
    {
        var esperadas = new HashSet<(EstadoDoDocumento, EstadoDoDocumento)>
        {
            (EstadoDoDocumento.Recebido, EstadoDoDocumento.Extraindo),
            (EstadoDoDocumento.Extraindo, EstadoDoDocumento.Extraido),
            (EstadoDoDocumento.Extraindo, EstadoDoDocumento.RequerRevisao),
            (EstadoDoDocumento.Extraindo, EstadoDoDocumento.Falhou),
            (EstadoDoDocumento.Extraindo, EstadoDoDocumento.Recebido),
            (EstadoDoDocumento.Extraido, EstadoDoDocumento.RequerRevisao),
            (EstadoDoDocumento.Extraido, EstadoDoDocumento.Pago),
            (EstadoDoDocumento.RequerRevisao, EstadoDoDocumento.Revisado),
            (EstadoDoDocumento.Revisado, EstadoDoDocumento.Pago),
            (EstadoDoDocumento.Falhou, EstadoDoDocumento.Recebido),
        };

        foreach (var de in Enum.GetValues<EstadoDoDocumento>())
        {
            foreach (var para in Enum.GetValues<EstadoDoDocumento>())
            {
                Assert.Equal(esperadas.Contains((de, para)), MaquinaDeEstadosDoDocumento.Aceita(de, para));
            }
        }
    }

    /// <summary>
    /// Pago é terminal. O dinheiro já saiu da conta, e reabrir o documento não desfaz o
    /// lançamento — só abriria caminho para pagar de novo.
    /// </summary>
    [Theory]
    [InlineData(EstadoDoDocumento.Recebido)]
    [InlineData(EstadoDoDocumento.Extraindo)]
    [InlineData(EstadoDoDocumento.RequerRevisao)]
    [InlineData(EstadoDoDocumento.Pago)]
    public void NadaSaiDePago(EstadoDoDocumento destino) =>
        Assert.False(MaquinaDeEstadosDoDocumento.Aceita(EstadoDoDocumento.Pago, destino));

    [Fact]
    public void TransicaoProibidaEhRecusadaComExcecao() =>
        Assert.Throws<TransicaoInvalidaException>(
            () => MaquinaDeEstadosDoDocumento.Garantir(
                EstadoDoDocumento.Recebido,
                EstadoDoDocumento.Pago));
}
