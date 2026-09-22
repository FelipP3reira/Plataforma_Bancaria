using Banco.Dominio.Erros;

namespace Banco.Dominio.Documentos;

/// <summary>
/// Um documento financeiro recebido para extracao.
/// </summary>
/// <remarks>
/// O agregado nao guarda os bytes: guarda onde eles estao. Arquivo de dez megabytes dentro
/// da linha faria toda consulta de lista carregar o conteudo junto, e o banco viraria
/// deposito de blob por acidente.
/// </remarks>
public sealed class Documento
{
    public const int TamanhoMaximoDoNome = 200;
    public const long TamanhoMaximoEmBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Quantas extracoes o documento tenta antes de desistir.
    /// </summary>
    /// <remarks>
    /// Existe porque o servico de extracao e de terceiro: instabilidade de rede passa na
    /// segunda, mas PDF que derruba o extrator derrubaria tambem na milesima. Sem teto, um
    /// documento assim ocuparia o worker para sempre e nenhum outro andaria.
    /// </remarks>
    public const int MaximoDeTentativas = 3;

    public const int TamanhoMaximoDoErro = 300;

    private Documento()
    {
    }

    private Documento(
        Guid contaId,
        string nomeOriginal,
        TipoDeArquivo tipo,
        long tamanhoEmBytes,
        HashDoArquivo hash,
        string caminhoRelativo,
        string origem,
        DateTimeOffset recebidoEm)
    {
        Id = Guid.CreateVersion7();
        ContaId = contaId;
        NomeOriginal = nomeOriginal;
        Tipo = tipo;
        TamanhoEmBytes = tamanhoEmBytes;
        Hash = hash;
        CaminhoRelativo = caminhoRelativo;
        Origem = origem;
        Estado = EstadoDoDocumento.Recebido;
        RecebidoEm = recebidoEm;
        AtualizadoEm = recebidoEm;
    }

    public Guid Id { get; private set; }

    /// <summary>A conta a que o documento pertence, e de onde sairia o pagamento.</summary>
    public Guid ContaId { get; private set; }

    /// <summary>
    /// O nome que o arquivo tinha na maquina de quem enviou. So para exibir.
    /// </summary>
    /// <remarks>
    /// Nunca entra no caminho de gravacao. Esse campo e escolhido por quem envia, e
    /// <c>../../appsettings.json</c> e um nome de arquivo valido — o caminho real e
    /// derivado do hash, que ninguem escolhe.
    /// </remarks>
    public string NomeOriginal { get; private set; } = string.Empty;

    public TipoDeArquivo Tipo { get; private set; }

    public long TamanhoEmBytes { get; private set; }

    public HashDoArquivo Hash { get; private set; }

    /// <summary>
    /// Onde os bytes ficaram, relativo a raiz do armazenamento.
    /// </summary>
    /// <remarks>
    /// Gravado em vez de derivado do hash a cada leitura: o dia em que o layout de pastas
    /// mudar, ou o armazenamento virar S3, os arquivos antigos continuam alcancaveis pelo
    /// caminho que eles de fato tem. Caminho derivado obrigaria a mover tudo junto.
    /// </remarks>
    public string CaminhoRelativo { get; private set; } = string.Empty;

    /// <summary>Quem enviou. Mesmo papel do <c>X-Operador</c> nas movimentacoes.</summary>
    public string Origem { get; private set; } = string.Empty;

    public EstadoDoDocumento Estado { get; private set; }

    public DateTimeOffset RecebidoEm { get; private set; }

    public DateTimeOffset AtualizadoEm { get; private set; }

    /// <summary>
    /// Ate quando a reserva do worker vale. Nulo quando ninguem esta com o documento.
    /// </summary>
    /// <remarks>
    /// Reserva com prazo, e nao bandeira de "em processamento": worker que morre no meio
    /// nao devolve nada, e uma bandeira sem prazo deixaria o documento preso em
    /// <see cref="EstadoDoDocumento.Extraindo"/> ate alguem notar na mao. Com prazo, a
    /// propria fila recupera.
    /// </remarks>
    public DateTimeOffset? LeaseAte { get; private set; }

    public int Tentativas { get; private set; }

    /// <summary>
    /// Por que a ultima tentativa nao deu certo. So mensagem nossa.
    /// </summary>
    /// <remarks>
    /// Nunca recebe o conteudo extraido nem resposta crua do extrator: campo de erro e o
    /// que mais aparece em log e em tela de suporte, e boleto carrega CNPJ, valor e nome.
    /// </remarks>
    public string? UltimoErro { get; private set; }

    /// <summary>O texto que o extrator leu do arquivo. Dado sensivel.</summary>
    public string? ConteudoExtraido { get; private set; }

    /// <summary>Quanto o extrator confia no que leu, de 0 a 1.</summary>
    public decimal? Confianca { get; private set; }

    public DateTimeOffset? ExtraidoEm { get; private set; }

    /// <summary>A reserva expirou: o worker que a pegou nao voltou.</summary>
    public bool LeaseVencidoEm(DateTimeOffset agora) =>
        Estado == EstadoDoDocumento.Extraindo && LeaseAte <= agora;

    /// <summary>Reserva o documento para uma tentativa de extracao.</summary>
    public void Reservar(DateTimeOffset agora, TimeSpan prazo)
    {
        Transicionar(EstadoDoDocumento.Extraindo, agora);

        // A tentativa conta na reserva, e nao no fim. Worker que morre antes de chegar ao
        // fim nao grava nada — se o contador ficasse para depois, o documento que mata o
        // processo voltaria para a fila com tentativa zero, para sempre.
        Tentativas++;
        LeaseAte = agora + prazo;
    }

    public void Concluir(string conteudoExtraido, decimal confianca, DateTimeOffset agora)
    {
        if (confianca is < 0m or > 1m)
        {
            throw new ArquivoRecusadoException($"Confianca fora da faixa de 0 a 1: {confianca}.");
        }

        Transicionar(EstadoDoDocumento.Extraido, agora);

        ConteudoExtraido = conteudoExtraido;
        Confianca = confianca;
        ExtraidoEm = agora;
        LeaseAte = null;
        UltimoErro = null;
    }

    /// <summary>
    /// Registra o fracasso desta tentativa: volta para a fila se ainda sobra tentativa, ou
    /// desiste.
    /// </summary>
    /// <remarks>
    /// A decisao mora num lugar so de proposito. Espalhada entre o worker e a varredura de
    /// reserva vencida, ela divergiria — e divergir aqui significa ou documento tentando
    /// eternamente, ou documento desistindo na primeira falha de rede.
    /// </remarks>
    public void Falhar(string motivo, DateTimeOffset agora)
    {
        var desiste = Tentativas >= MaximoDeTentativas;

        Transicionar(desiste ? EstadoDoDocumento.Falhou : EstadoDoDocumento.Recebido, agora);

        LeaseAte = null;
        UltimoErro = Encurtar(motivo);
    }

    /// <summary>Devolve a fila um documento que desistiu. Decisao de gente.</summary>
    /// <remarks>
    /// Exige <see cref="EstadoDoDocumento.Falhou"/> alem do que a maquina de transicoes
    /// permite. A aresta que sai de <see cref="EstadoDoDocumento.Extraindo"/> existe para o
    /// prazo de reserva vencido, e sem esta guarda o reprocessamento manual pegaria carona
    /// nela — arrancando da mao de um worker um documento que ele ainda esta extraindo, e
    /// deixando dois processando o mesmo arquivo.
    /// </remarks>
    public void Reenfileirar(DateTimeOffset agora)
    {
        if (Estado != EstadoDoDocumento.Falhou)
        {
            throw new TransicaoInvalidaException(
                $"So documento que desistiu volta a fila a mao; este esta em {Estado}.");
        }

        Transicionar(EstadoDoDocumento.Recebido, agora);

        // O contador zera junto: sem isso o documento reenfileirado gastaria a unica
        // tentativa que sobrou e voltaria a Falhou na primeira instabilidade, e quem
        // reprocessou concluiria que o problema e o arquivo.
        Tentativas = 0;
        LeaseAte = null;
    }

    private void Transicionar(EstadoDoDocumento destino, DateTimeOffset agora)
    {
        MaquinaDeEstadosDoDocumento.Garantir(Estado, destino);

        Estado = destino;
        AtualizadoEm = agora;
    }

    private static string Encurtar(string motivo)
    {
        var limpo = new string([.. motivo.Where(caractere => !char.IsControl(caractere))]).Trim();

        return limpo.Length > TamanhoMaximoDoErro ? limpo[..TamanhoMaximoDoErro] : limpo;
    }

    public static Documento Receber(
        Guid contaId,
        string nomeOriginal,
        TipoDeArquivo tipo,
        long tamanhoEmBytes,
        HashDoArquivo hash,
        string caminhoRelativo,
        string origem,
        DateTimeOffset recebidoEm)
    {
        if (tamanhoEmBytes <= 0)
        {
            throw new ArquivoRecusadoException("Arquivo vazio.");
        }

        if (tamanhoEmBytes > TamanhoMaximoEmBytes)
        {
            throw new ArquivoRecusadoException(
                $"Arquivo acima de {TamanhoMaximoEmBytes / (1024 * 1024)} MB.");
        }

        if (string.IsNullOrWhiteSpace(origem))
        {
            throw new ArquivoRecusadoException("Origem obrigatoria: documento sem dono nao se audita.");
        }

        if (string.IsNullOrWhiteSpace(caminhoRelativo))
        {
            throw new ArquivoRecusadoException("Documento sem caminho de arquivo nao tem o que extrair.");
        }

        return new Documento(
            contaId,
            Higienizar(nomeOriginal, tipo),
            tipo,
            tamanhoEmBytes,
            hash,
            caminhoRelativo,
            origem.Trim(),
            recebidoEm);
    }

    /// <summary>
    /// Deixa o nome apresentavel sem confiar nele.
    /// </summary>
    /// <remarks>
    /// Tira diretorio, caractere de controle e o que o sistema de arquivos recusa, e corta
    /// no limite. Mesmo assim o resultado nao vira caminho em lugar nenhum — isto aqui e
    /// para o nome nao quebrar a tela nem carregar um <c>\r\n</c> para dentro de um log.
    /// </remarks>
    private static string Higienizar(string nome, TipoDeArquivo tipo)
    {
        var semCaminho = Path.GetFileName(nome?.Trim() ?? string.Empty);

        var limpo = new string([.. semCaminho
            .Where(caractere => !char.IsControl(caractere) && Array.IndexOf(Path.GetInvalidFileNameChars(), caractere) < 0)]);

        if (limpo.Length > TamanhoMaximoDoNome)
        {
            limpo = limpo[..TamanhoMaximoDoNome];
        }

        return string.IsNullOrWhiteSpace(limpo)
            ? $"documento{ReconhecedorDeArquivo.ExtensaoDe(tipo)}"
            : limpo;
    }
}
