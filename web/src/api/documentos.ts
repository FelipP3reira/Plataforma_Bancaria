import { BANCO, ErroDaApi, chamar } from "./cliente";

export type EstadoDoDocumento =
  | "Recebido"
  | "Extraindo"
  | "Extraido"
  | "RequerRevisao"
  | "Revisado"
  | "Pago"
  | "Falhou";

export type NomeDoCampo = "LinhaDigitavel" | "Valor" | "Vencimento" | "CnpjDoEmissor";

export type CampoDoDocumento = {
  nome: NomeDoCampo;
  valorLido: string;
  valorFinal: string;
  confianca: number;
  origem: "Estrutura" | "Texto";
  observacao: string | null;
  corrigidoPor: string | null;
  corrigidoEm: string | null;
};

export type DocumentoEnviado = {
  id: string;
  nomeOriginal: string;
  tipo: string;
  tamanhoEmBytes: number;
  hash: string;
  estado: EstadoDoDocumento;
  /** Falso quando o arquivo já tinha sido enviado nesta conta: é o mesmo documento. */
  novo: boolean;
};

export type Documento = {
  id: string;
  contaId: string;
  nomeOriginal: string;
  tipo: string;
  tamanhoEmBytes: number;
  hash: string;
  estado: EstadoDoDocumento;
  origem: string;
  recebidoEm: string;
  atualizadoEm: string;
  tentativas: number;
  ultimoErro: string | null;
  /** A confiança da estrutura lida. É ela que decide se o documento precisa de olho humano. */
  confianca: number | null;
  /** A confiança do extrator sobre a própria leitura do arquivo. Responde outra pergunta. */
  confiancaDoTexto: number | null;
  extraidoEm: string | null;
  conteudoExtraido: string | null;
  lancamentoDoPagamentoId: string | null;
  pagoEm: string | null;
  campos: CampoDoDocumento[];
};

export type PagamentoDoDocumento = {
  documentoId: string;
  lancamentoId: string;
  valor: number;
  linhaDigitavel: string;
  estado: EstadoDoDocumento;
  novo: boolean;
};

const banco = <T>(rota: string, opcoes?: Parameters<typeof chamar>[2]) =>
  chamar<T>(BANCO, rota, opcoes);

/**
 * Envia o arquivo.
 *
 * Fora do `chamar` do resto da API porque o corpo é `multipart`, e não JSON: `FormData`
 * precisa que o navegador monte o `Content-Type` com a fronteira, e escrever esse cabeçalho
 * à mão quebra o upload de um jeito que o erro não explica.
 *
 * E sem `Idempotency-Key`, ao contrário das movimentações: aqui a chave é o próprio
 * conteúdo. O servidor calcula o SHA-256 do arquivo, e o mesmo boleto enviado duas vezes
 * volta como o mesmo documento com `novo: false`.
 */
export async function enviarDocumento(
  contaId: string,
  arquivo: File,
  operador: string,
): Promise<DocumentoEnviado> {
  const corpo = new FormData();
  corpo.append("contaId", contaId);
  corpo.append("arquivo", arquivo);

  const resposta = await fetch(`${BANCO}/documentos`, {
    method: "POST",
    headers: { "X-Operador": operador },
    body: corpo,
  }).catch(() => null);

  if (!resposta) {
    throw new ErroDaApi(0, "Sem resposta", `Não consegui falar com ${BANCO}. A API está no ar?`);
  }

  if (!resposta.ok) {
    const problema = await resposta.json().catch(() => ({}) as Record<string, string>);

    throw new ErroDaApi(
      resposta.status,
      problema.title ?? `Erro ${resposta.status}`,
      problema.detail ?? "A API não explicou o motivo.",
    );
  }

  return (await resposta.json()) as DocumentoEnviado;
}

export const documento = (id: string) => banco<Documento>(`/documentos/${id}`);

export const revisar = (
  id: string,
  correcoes: Partial<Record<NomeDoCampo, string>>,
  operador: string,
) =>
  banco<void>(`/documentos/${id}/revisao`, {
    metodo: "POST",
    corpo: { correcoes },
    operador,
  });

export const pagarDocumento = (id: string, operador: string) =>
  banco<PagamentoDoDocumento>(`/documentos/${id}/pagamento`, { metodo: "POST", operador });

export const reprocessarDocumento = (id: string, operador: string) =>
  banco<void>(`/documentos/${id}/reprocessamento`, { metodo: "POST", operador });

/** O rótulo de cada campo. O nome da API é em inglês de código; a tela fala português. */
export const ROTULOS: Record<NomeDoCampo, string> = {
  LinhaDigitavel: "Linha digitável",
  Valor: "Valor",
  Vencimento: "Vencimento",
  CnpjDoEmissor: "CNPJ do emissor",
};

/**
 * O que a pessoa pode fazer com o documento no estado em que ele está.
 *
 * Tabela e não uma sequência de `if`: é o mesmo desenho da máquina de estados do servidor, e
 * a tela que adivinha por conta própria mostra botão que a API vai recusar.
 */
export const ESTADOS: Record<EstadoDoDocumento, { rotulo: string; espera: boolean }> = {
  Recebido: { rotulo: "Na fila", espera: true },
  Extraindo: { rotulo: "Lendo", espera: true },
  Extraido: { rotulo: "Lido", espera: false },
  RequerRevisao: { rotulo: "Precisa conferir", espera: false },
  Revisado: { rotulo: "Conferido", espera: false },
  Pago: { rotulo: "Pago", espera: false },
  Falhou: { rotulo: "Falhou", espera: false },
};
