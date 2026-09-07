/**
 * A borda entre a interface e as duas APIs.
 *
 * Existe para que nenhuma tela precise saber montar cabeçalho, ler ProblemDetails ou
 * decidir quando gerar chave de idempotência. Tela que faz isso sozinha acaba esquecendo
 * de um dos três — e o que ela esquece é sempre a chave.
 */

export const BANCO = import.meta.env.VITE_API_BANCO ?? "http://localhost:5240";
export const CREDITO = import.meta.env.VITE_API_CREDITO ?? "http://localhost:5250";

/**
 * Uma recusa que a API explicou.
 *
 * Guarda o título separado do detalhe porque as telas usam os dois de formas diferentes:
 * o título vira o cabeçalho do aviso, o detalhe vira a frase. Concatenar os dois na
 * mensagem devolveria texto que não dá para formatar depois.
 */
export class ErroDaApi extends Error {
  constructor(
    readonly status: number,
    readonly titulo: string,
    readonly detalhe: string,
  ) {
    super(titulo);
    this.name = "ErroDaApi";
  }

  /** Recusa por estado ou regra: repetir igual não muda nada. */
  get ehRecusa() {
    return this.status >= 400 && this.status < 500;
  }
}

type ProblemDetails = { title?: string; detail?: string; errors?: Record<string, string[]> };

async function traduzir(resposta: Response): Promise<ErroDaApi> {
  let corpo: ProblemDetails = {};

  try {
    corpo = await resposta.json();
  } catch {
    // Resposta sem corpo ou com corpo que não é JSON ainda diz algo pelo status. O que
    // não pode é a falha de leitura virar a falha que a pessoa vê na tela.
  }

  // Erro de validação vem como um mapa de campo para mensagens; a pessoa precisa das
  // mensagens, e não da palavra "validação".
  const doCampo = corpo.errors
    ? Object.values(corpo.errors).flat().join(" ")
    : "";

  return new ErroDaApi(
    resposta.status,
    corpo.title ?? `Erro ${resposta.status}`,
    doCampo || corpo.detail || "A API não explicou o motivo.",
  );
}

type Opcoes = {
  metodo?: "GET" | "POST";
  corpo?: unknown;
  /** Chave de idempotência. Quem passa é quem sabe o que é uma repetição do quê. */
  chave?: string;
  operador?: string;
};

export async function chamar<T>(base: string, rota: string, opcoes: Opcoes = {}): Promise<T> {
  const cabecalhos: Record<string, string> = {};

  if (opcoes.corpo !== undefined) cabecalhos["Content-Type"] = "application/json";
  if (opcoes.chave) cabecalhos["Idempotency-Key"] = opcoes.chave;
  if (opcoes.operador) cabecalhos["X-Operador"] = opcoes.operador;

  let resposta: Response;

  try {
    resposta = await fetch(`${base}${rota}`, {
      method: opcoes.metodo ?? "GET",
      headers: cabecalhos,
      body: opcoes.corpo === undefined ? undefined : JSON.stringify(opcoes.corpo),
    });
  } catch {
    // API fora do ar e CORS bloqueado chegam aqui do mesmo jeito: o navegador não conta a
    // diferença. Dizer "sem resposta" é honesto; dizer "erro de rede" mandaria a pessoa
    // olhar o wi-fi quando o problema é a API não ter subido.
    throw new ErroDaApi(0, "Sem resposta", `Não consegui falar com ${base}. A API está no ar?`);
  }

  if (!resposta.ok) throw await traduzir(resposta);
  if (resposta.status === 204) return undefined as T;

  return (await resposta.json()) as T;
}

/**
 * Uma chave nova, para uma operação nova.
 *
 * Gerada aqui, no cliente, e é isso que o cabeçalho pede. O ponto dela aparece no retry:
 * a mesma chave reapresentada faz a API devolver o lançamento que já existe em vez de
 * criar um segundo. Por isso as telas geram a chave ANTES de mostrar o botão, e não no
 * clique — quem clica duas vezes manda a mesma chave duas vezes.
 */
export const chaveNova = () => crypto.randomUUID();

export const dinheiro = (valor: number) =>
  valor.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });

export const dataHora = (iso: string) =>
  new Date(iso).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" });

export const data = (iso: string) =>
  new Date(`${iso.slice(0, 10)}T12:00:00`).toLocaleDateString("pt-BR");
