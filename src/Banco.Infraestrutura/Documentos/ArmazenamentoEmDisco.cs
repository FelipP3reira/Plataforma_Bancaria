using Banco.Aplicacao.Portas;
using Banco.Dominio.Documentos;
using Banco.Dominio.Erros;
using Microsoft.Extensions.Options;

namespace Banco.Infraestrutura.Documentos;

/// <summary>
/// Os bytes em disco, com o caminho derivado do hash.
/// </summary>
/// <remarks>
/// O nome do arquivo e o proprio hash. Isso resolve tres coisas de uma vez: nada do que
/// quem envia escolheu entra no caminho, o mesmo conteudo sempre cai no mesmo lugar, e dois
/// arquivos de nome igual nao se sobrescrevem.
/// <para>
/// Os dois primeiros caracteres viram pasta. Sem isso, uma base com dezenas de milhares de
/// documentos joga todos num diretorio so — o que alguns sistemas de arquivos aguentam mal
/// e todo <c>ls</c> sofre.
/// </para>
/// </remarks>
public sealed class ArmazenamentoEmDisco : IArmazenamentoDeDocumentos
{
    private readonly string raiz;

    public ArmazenamentoEmDisco(IOptions<OpcoesDeArmazenamento> opcoes)
    {
        ArgumentNullException.ThrowIfNull(opcoes);
        raiz = Path.GetFullPath(opcoes.Value.Raiz);
    }

    public async Task<string> Guardar(
        HashDoArquivo hash,
        TipoDeArquivo tipo,
        Stream conteudo,
        CancellationToken cancelamento)
    {
        var relativo = CaminhoDe(hash, tipo);
        var completo = Resolver(relativo);

        Directory.CreateDirectory(Path.GetDirectoryName(completo)!);

        // FileMode.Create e nao CreateNew: o mesmo hash significa o mesmo conteudo, entao
        // reescrever e inofensivo. Recusar obrigaria a tratar como erro o reenvio que o
        // caso de uso ja trata como repeticao.
        await using var destino = new FileStream(
            completo,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await conteudo.CopyToAsync(destino, cancelamento).ConfigureAwait(false);

        return relativo;
    }

    public Task<Stream?> Abrir(string caminhoRelativo, CancellationToken cancelamento)
    {
        var completo = Resolver(caminhoRelativo);

        if (!File.Exists(completo))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new FileStream(
            completo,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true));
    }

    private static string CaminhoDe(HashDoArquivo hash, TipoDeArquivo tipo) =>
        $"{hash.Texto[..2]}/{hash.Texto}{ReconhecedorDeArquivo.ExtensaoDe(tipo)}";

    /// <summary>
    /// Transforma o caminho relativo em absoluto e confere que ele nao escapou da raiz.
    /// </summary>
    /// <remarks>
    /// Os caminhos daqui sao derivados do hash e nao teriam como escapar. A conferencia
    /// existe porque este metodo tambem recebe o que veio gravado na linha do banco, e
    /// linha de banco e dado — se um dia alguem conseguir escrever nela, o caminho de
    /// leitura nao pode virar um jeito de ler qualquer arquivo do servidor.
    /// </remarks>
    private string Resolver(string caminhoRelativo)
    {
        var completo = Path.GetFullPath(Path.Combine(raiz, caminhoRelativo));

        if (!completo.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArquivoRecusadoException("Caminho de documento fora da pasta de armazenamento.");
        }

        return completo;
    }
}
