import { CREDITO, chamar } from "./cliente";

/**
 * O serviço de crédito.
 *
 * Fica num arquivo separado do banco porque são dois serviços de verdade, com endereços
 * próprios e disponibilidade própria. Juntar os dois num cliente só faria parecer que a
 * área de empréstimo cai junto com o extrato — e não cai.
 */

export type Sistema = "Price" | "Sac";

export type ProximaParcela = {
  numero: number | null;
  vencimento: string | null;
  valor: number | null;
};

export type ContratoDaConta = {
  contratoId: string;
  propostaId: string;
  valorFinanciado: number;
  taxaMensal: number;
  sistema: Sistema;
  prazoEmMeses: number;
  totalPago: number;
  saldoAberto: number;
  parcelasPagas: number;
  estaQuitado: boolean;
  assinadoEm: string;
  desembolsadoEm: string | null;
  proxima: ProximaParcela;
};

export type ParcelaDoContrato = {
  numero: number;
  vencimento: string;
  amortizacao: number;
  juros: number;
  valor: number;
  saldoDevedor: number;
  pagaEm: string | null;
};

export type Contrato = {
  id: string;
  propostaId: string;
  contaId: string | null;
  desembolsadoEm: string | null;
  valorFinanciado: number;
  taxaMensal: number;
  sistema: Sistema;
  prazoEmMeses: number;
  primeiroVencimento: string;
  assinadoEm: string;
  totalPago: number;
  saldoAberto: number;
  estaQuitado: boolean;
  parcelas: ParcelaDoContrato[];
};

export type Simulacao = {
  propostaId: string;
  valorFinanciado: number;
  taxaMensal: number;
  prazoEmMeses: number;
  sistema: Sistema;
  scoreObservado: number;
  primeiraParcela: number;
  ultimaParcela: number;
  totalPago: number;
  totalDeJuros: number;
  parcelas: { numero: number; amortizacao: number; juros: number; valor: number; saldoDevedor: number }[];
};

export type LinhaDoLaudo = {
  codigo: string;
  aprovou: boolean;
  motivo: string;
  valorObservado: number | null;
  limiteExigido: number | null;
};

export type Decisao = {
  propostaId: string;
  estado: string;
  aprovada: boolean;
  scoreObservado: number;
  taxaMensalAplicada: number;
  versaoDaPolitica: number;
  avaliadaEm: string;
  laudo: LinhaDoLaudo[];
};

export type PropostaCadastrada = { id: string; estado: string };

export type Desembolso = {
  contratoId: string;
  contaId: string;
  valor: number;
  lancamentoId: string;
  saldoDaConta: number;
  novo: boolean;
};

export type PagamentoRegistrado = {
  numero: number;
  valor: number;
  totalPago: number;
  saldoAberto: number;
  contratoQuitado: boolean;
  saldoDaConta: number | null;
};

export type PedidoDeEmprestimo = {
  cpf: string;
  nomeSolicitante: string;
  dataDeNascimento: string;
  rendaMensal: number;
  valorSolicitado: number;
  prazoEmMeses: number;
  sistema: Sistema;
};

const credito = <T>(rota: string, opcoes?: Parameters<typeof chamar>[2]) =>
  chamar<T>(CREDITO, rota, opcoes);

export const contratosDaConta = (contaId: string) =>
  credito<ContratoDaConta[]>(`/contratos?contaId=${contaId}`);

export const contrato = (propostaId: string) =>
  credito<Contrato>(`/propostas/${propostaId}/contrato`);

export const cadastrarProposta = (pedido: PedidoDeEmprestimo, chave: string) =>
  credito<PropostaCadastrada>("/propostas", { metodo: "POST", corpo: pedido, chave });

export const analisar = (propostaId: string) =>
  credito<Decisao>(`/propostas/${propostaId}/analise`, { metodo: "POST" });

export const simular = (propostaId: string) =>
  credito<Simulacao>(`/propostas/${propostaId}/simulacao`, { metodo: "POST" });

export const contratarNaConta = (propostaId: string, contaId: string) =>
  credito<Contrato>(`/propostas/${propostaId}/contrato`, {
    metodo: "POST",
    corpo: { contaId },
  });

/**
 * O desembolso não leva chave de idempotência daqui.
 *
 * A chave é derivada do contrato do lado de lá, justamente para ser sempre a mesma. Se a
 * interface mandasse uma chave nova a cada clique, dois cliques virariam dois empréstimos
 * creditados.
 */
export const desembolsar = (propostaId: string) =>
  credito<Desembolso>(`/propostas/${propostaId}/contrato/desembolso`, { metodo: "POST" });

export const pagarParcela = (propostaId: string, numero: number, chave: string) =>
  credito<PagamentoRegistrado>(
    `/propostas/${propostaId}/contrato/parcelas/${numero}/pagamento`,
    { metodo: "POST", chave },
  );
