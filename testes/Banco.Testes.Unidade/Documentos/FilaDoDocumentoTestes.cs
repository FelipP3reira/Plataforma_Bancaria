using System.Text;
using Banco.Dominio.Documentos;
using Banco.Dominio.Documentos.Boletos;
using Banco.Dominio.Erros;

namespace Banco.Testes.Unidade.Documentos;

/// <summary>
/// A mecanica da fila: reserva com prazo, tentativas contadas e a desistencia no fim.
/// </summary>
public class FilaDoDocumentoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Prazo = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Limiar zero: nada cai na revisao. Os testes desta classe sao sobre a fila, e o
    /// encaminhamento por confianca tem os seus.
    /// </summary>
    private const decimal TudoPassa = 0m;

    private static Documento Recebido() =>
        Documento.Receber(
            Guid.CreateVersion7(),
            "boleto.pdf",
            TipoDeArquivo.Pdf,
            1024,
            HashDoArquivo.De(Encoding.UTF8.GetBytes("boleto")),
            "ab/abc.pdf",
            "web:Ana Ribeiro",
            Agora);

    private static Documento Reservado(DateTimeOffset? quando = null)
    {
        var documento = Recebido();
        documento.Reservar(quando ?? Agora, Prazo);

        return documento;
    }

    [Fact]
    public void ReservarPoeOPrazoEContaATentativa()
    {
        var documento = Reservado();

        Assert.Equal(EstadoDoDocumento.Extraindo, documento.Estado);
        Assert.Equal(Agora + Prazo, documento.LeaseAte);
        Assert.Equal(1, documento.Tentativas);
    }

    /// <summary>
    /// A tentativa conta na reserva, e nao no fim. Worker que morre antes de gravar o
    /// resultado nao teria como incrementar nada — e o documento que mata o processo
    /// voltaria para a fila com tentativa zero, para sempre.
    /// </summary>
    [Fact]
    public void TentativaContaMesmoSemResultado()
    {
        var documento = Reservado();
        documento.Falhar("worker caiu", Agora);
        documento.Reservar(Agora, Prazo);

        Assert.Equal(2, documento.Tentativas);
    }

    [Fact]
    public void DocumentoReservadoNaoPodeSerReservadoDeNovo() =>
        Assert.Throws<TransicaoInvalidaException>(() => Reservado().Reservar(Agora, Prazo));

    [Fact]
    public void OPrazoVenceQuandoOInstanteChega()
    {
        var documento = Reservado();

        Assert.False(documento.LeaseVencidoEm(Agora + Prazo - TimeSpan.FromSeconds(1)));
        Assert.True(documento.LeaseVencidoEm(Agora + Prazo));
    }

    // Prazo vencido so significa algo para quem esta reservado: documento na fila nao tem
    // reserva nenhuma para vencer.
    [Fact]
    public void DocumentoNaFilaNaoTemPrazoVencido() =>
        Assert.False(Recebido().LeaseVencidoEm(Agora.AddYears(1)));

    /// <summary>
    /// As duas confiancas ficam guardadas separadas: a do extrator sobre a propria leitura, e a
    /// da estrutura sobre o que foi lido. E a da estrutura que vale como confianca do documento.
    /// </summary>
    [Fact]
    public void ConcluirGuardaOTextoEAsDuasConfiancas()
    {
        var documento = Reservado();
        documento.Concluir("linha digitavel", confiancaDoTexto: 0.93m, Leitura(0.40m), TudoPassa, Agora);

        Assert.Equal(EstadoDoDocumento.Extraido, documento.Estado);
        Assert.Equal("linha digitavel", documento.ConteudoExtraido);
        Assert.Equal(0.93m, documento.ConfiancaDoTexto);
        Assert.Equal(0.40m, documento.Confianca);
        Assert.Equal(Agora, documento.ExtraidoEm);
    }

    [Fact]
    public void ConcluirGuardaOsCamposLidos()
    {
        var documento = Reservado();
        documento.Concluir("texto", 1m, Leitura(1m, NomeDoCampo.Valor, NomeDoCampo.Vencimento), TudoPassa, Agora);

        Assert.Equal(
            [NomeDoCampo.Valor, NomeDoCampo.Vencimento],
            documento.Campos.Select(campo => campo.Nome));
    }

    /// <summary>
    /// Documento extraido nao volta para a fila: nao ha aresta de Extraido para Recebido. E o
    /// que garante que a leitura de um documento nunca e sobrescrita pela metade.
    /// </summary>
    [Fact]
    public void DocumentoExtraidoNaoVoltaParaAFila()
    {
        var documento = Reservado();
        documento.Concluir("texto", 1m, Leitura(1m, NomeDoCampo.Valor), TudoPassa, Agora);

        Assert.Throws<TransicaoInvalidaException>(() => documento.Falhar("de novo", Agora));
    }

    /// <summary>
    /// A reserva e limpa no fim. Se ela ficasse, a varredura de prazo vencido acharia um
    /// documento ja extraido e o mandaria de volta para a fila.
    /// </summary>
    [Fact]
    public void ConcluirSoltaAReserva() =>
        Assert.Null(ConcluidoCom(0.9m).LeaseAte);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void ConfiancaDoTextoForaDaFaixaEhRecusada(double confianca) =>
        Assert.Throws<ArquivoRecusadoException>(
            () => Reservado().Concluir("texto", (decimal)confianca, Leitura(1m), TudoPassa, Agora));

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void ConfiancaDaLeituraForaDaFaixaEhRecusada(double confianca) =>
        Assert.Throws<ArquivoRecusadoException>(
            () => Reservado().Concluir("texto", 1m, Leitura((decimal)confianca), TudoPassa, Agora));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ConfiancaNasPontasDaFaixaPassa(double confianca) =>
        Assert.Equal((decimal)confianca, ConcluidoCom((decimal)confianca).Confianca);

    /// <summary>
    /// Falha com tentativa sobrando volta para a fila. E a instabilidade de rede que passa
    /// na segunda vez.
    /// </summary>
    [Fact]
    public void FalhaComTentativaSobrandoVoltaParaAFila()
    {
        var documento = Reservado();
        documento.Falhar("HttpRequestException", Agora);

        Assert.Equal(EstadoDoDocumento.Recebido, documento.Estado);
        Assert.Null(documento.LeaseAte);
        Assert.Equal("HttpRequestException", documento.UltimoErro);
    }

    /// <summary>
    /// Esgotadas as tentativas, o documento sai da fila. Sem teto, um arquivo que derruba o
    /// extrator ocuparia o worker para sempre e nenhum outro andaria.
    /// </summary>
    [Fact]
    public void NaUltimaTentativaODocumentoDesiste()
    {
        var documento = Recebido();

        for (var tentativa = 1; tentativa <= Documento.MaximoDeTentativas; tentativa++)
        {
            documento.Reservar(Agora, Prazo);
            documento.Falhar("InvalidDataException", Agora);
        }

        Assert.Equal(EstadoDoDocumento.Falhou, documento.Estado);
        Assert.Equal(Documento.MaximoDeTentativas, documento.Tentativas);
    }

    /// <summary>
    /// A mensagem gravada e curta e sem controle: campo de erro e o que mais aparece em log
    /// e em tela de suporte, e quebra de linha ali forja uma segunda entrada no log.
    /// </summary>
    [Fact]
    public void OMotivoDaFalhaEhLimpoECortado()
    {
        var documento = Reservado();
        documento.Falhar("erro\r\nINFO: tudo certo " + new string('a', 500), Agora);

        Assert.Equal(Documento.TamanhoMaximoDoErro, documento.UltimoErro!.Length);
        Assert.DoesNotContain('\n', documento.UltimoErro);
    }

    [Fact]
    public void ReenfileirarZeraOContadorEVoltaParaAFila()
    {
        var documento = Falhado();
        documento.Reenfileirar(Agora);

        Assert.Equal(EstadoDoDocumento.Recebido, documento.Estado);

        // Sem zerar, o documento reprocessado gastaria a unica tentativa que sobrou e
        // voltaria a Falhou na primeira instabilidade — e quem reprocessou concluiria que o
        // problema e o arquivo.
        Assert.Equal(0, documento.Tentativas);
    }

    [Fact]
    public void DocumentoQueEstaExtraindoNaoVoltaParaAFilaAMao() =>
        Assert.Throws<TransicaoInvalidaException>(() => Reservado().Reenfileirar(Agora));

    private static LeituraDoDocumento Leitura(decimal confianca, params NomeDoCampo[] nomes) =>
        new(
            [.. nomes.Select(nome => new CampoLido(nome, "1", confianca, OrigemDoCampo.Estrutura))],
            confianca);

    private static Documento ConcluidoCom(decimal confianca)
    {
        var documento = Reservado();
        documento.Concluir("texto", confianca, Leitura(confianca), TudoPassa, Agora);

        return documento;
    }

    private static Documento Falhado()
    {
        var documento = Recebido();

        for (var tentativa = 1; tentativa <= Documento.MaximoDeTentativas; tentativa++)
        {
            documento.Reservar(Agora, Prazo);
            documento.Falhar("InvalidDataException", Agora);
        }

        return documento;
    }
}
