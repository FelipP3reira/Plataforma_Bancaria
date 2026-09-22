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
