import { BANCO, chamar } from "./cliente";

export type EstadoDaConta = "Ativa" | "Bloqueada" | "Encerrada";

export type ContaAberta = {
  id: string;
  numero: string;
  titular: string;
  saldo: number;
  abertaEm: string;
};

export type Conta = ContaAberta & {
  estado: EstadoDaConta;
  lancamentos: number;
  atualizadaEm: string;
};

export type LinhaDoExtrato = {
  id: string;
  sequencia: number;
  tipo: "Credito" | "Debito";
  valor: number;
  efeito: number;
  saldoDepois: number;
  descricao: string;
  origem: string;
  criadoEm: string;
  transferenciaId: string | null;
};

export type PaginaDoExtrato = {
  contaId: string;
  numero: string;
  de: string;
  ate: string;
  linhas: LinhaDoExtrato[];
  proximaPagina: string | null;
};

export type MudancaDeEstado = {
  de: EstadoDaConta;
  para: EstadoDaConta;
  sequencia: number;
  motivo: string;
  origem: string;
  ocorridaEm: string;
};

export type Conciliacao = {
  saldoMaterializado: number;
  somaDoLedger: number;
  ultimaSequenciaDaConta: number;
  maiorSequenciaNoLedger: number;
  lancamentos: number;
  quebrasNaCorrente: number;
  diferenca: number;
  bate: boolean;
};

export type Movimentacao = {
  valor: number;
  descricao: string;
  chave: string;
  operador: string;
};

const banco = <T>(rota: string, opcoes?: Parameters<typeof chamar>[2]) =>
  chamar<T>(BANCO, rota, opcoes);

export const abrirConta = (titular: string) =>
  banco<ContaAberta>("/contas", { metodo: "POST", corpo: { titular } });

export const conta = (id: string) => banco<Conta>(`/contas/${id}`);

export const depositar = (id: string, m: Movimentacao) =>
  banco<LinhaDoExtrato>(`/contas/${id}/depositos`, {
    metodo: "POST",
    corpo: { valor: m.valor, descricao: m.descricao },
    chave: m.chave,
    operador: m.operador,
  });

export const sacar = (id: string, m: Movimentacao) =>
  banco<LinhaDoExtrato>(`/contas/${id}/saques`, {
    metodo: "POST",
    corpo: { valor: m.valor, descricao: m.descricao },
    chave: m.chave,
    operador: m.operador,
  });

export const transferir = (id: string, destino: string, m: Movimentacao) =>
  banco<{ id: string }>(`/contas/${id}/transferencias`, {
    metodo: "POST",
    corpo: { contaDestinoId: destino, valor: m.valor, descricao: m.descricao },
    chave: m.chave,
    operador: m.operador,
  });

/**
 * Uma página do extrato.
 *
 * O marcador é devolvido pela página anterior e reenviado como veio, sem interpretação:
 * é texto opaco de propósito, e ler o que tem dentro dele aqui amarraria a interface ao
 * formato interno do cursor.
 */
export function extrato(
  id: string,
  filtro: { de?: string; ate?: string; tamanho?: number; pagina?: string | null },
) {
  const parametros = new URLSearchParams();

  if (filtro.de) parametros.set("de", filtro.de);
  if (filtro.ate) parametros.set("ate", filtro.ate);
  if (filtro.tamanho) parametros.set("tamanho", String(filtro.tamanho));
  if (filtro.pagina) parametros.set("pagina", filtro.pagina);

  return banco<PaginaDoExtrato>(`/contas/${id}/extrato?${parametros}`);
}

export const conciliacao = (id: string) => banco<Conciliacao>(`/contas/${id}/conciliacao`);

export const historicoDeEstado = (id: string) =>
  banco<MudancaDeEstado[]>(`/contas/${id}/estados`);

const mudarEstado = (id: string, rota: string, motivo: string, operador: string) =>
  banco<MudancaDeEstado>(`/contas/${id}/${rota}`, {
    metodo: "POST",
    corpo: { motivo },
    operador,
  });

export const bloquear = (id: string, motivo: string, operador: string) =>
  mudarEstado(id, "bloqueio", motivo, operador);

export const desbloquear = (id: string, motivo: string, operador: string) =>
  mudarEstado(id, "desbloqueio", motivo, operador);

export const encerrar = (id: string, motivo: string, operador: string) =>
  mudarEstado(id, "encerramento", motivo, operador);
