import { useCallback, useEffect, useRef, useState } from "react";
import {
  analisar,
  cadastrarProposta,
  contratarNaConta,
  contratosDaConta,
  desembolsar,
  pagarParcela,
  simular,
  type ContratoDaConta,
  type Decisao,
  type Simulacao,
  type Sistema,
} from "../api/credito";
import { chaveNova, data, dinheiro } from "../api/cliente";
import { Aviso, Botao, Campo, Cartao, Carregando, entrada } from "../componentes/base";
import * as Icone from "../componentes/icones";
import type { Sessao } from "../sessao";

export default function Emprestimos({
  sessao,
  aoMudarSaldo,
}: {
  sessao: Sessao;
  aoMudarSaldo: () => void;
}) {
  const [contratos, setContratos] = useState<ContratoDaConta[] | null>(null);
  const [erro, setErro] = useState<unknown>(null);
  const [pedindo, setPedindo] = useState(false);

  const recarregar = useCallback(async () => {
    setErro(null);

    try {
      setContratos(await contratosDaConta(sessao.contaId));
    } catch (falha) {
      setErro(falha);
      setContratos([]);
    }
  }, [sessao.contaId]);

  useEffect(() => {
    void recarregar();
  }, [recarregar]);

  return (
    <div className="space-y-6">
      <Aviso erro={erro} aoFechar={() => setErro(null)} />

      {pedindo ? (
        <PedirEmprestimo
          sessao={sessao}
          aoFechar={() => setPedindo(false)}
          aoConcluir={() => {
            setPedindo(false);
            void recarregar();
            aoMudarSaldo();
          }}
        />
      ) : (
        <Cartao
          titulo="Meus empréstimos"
          acao={<Botao onClick={() => setPedindo(true)}>Pedir empréstimo</Botao>}
        >
          {contratos === null ? (
            <Carregando />
          ) : contratos.length === 0 ? (
            <div className="py-8 text-center">
              <span className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-marca/8 text-marca">
                <Icone.Cedula className="h-7 w-7" />
              </span>
              <p className="mt-4 font-medium">Nenhum empréstimo nesta conta</p>
              <p className="mx-auto mt-1 max-w-xs text-sm text-tinta-fraca">
                O valor aprovado cai direto aqui, e as parcelas saem desta mesma conta.
              </p>
            </div>
          ) : (
            <ul className="space-y-4">
              {contratos.map((contrato) => (
                <Emprestimo
                  key={contrato.contratoId}
                  contrato={contrato}
                  aoMudar={() => {
                    void recarregar();
                    aoMudarSaldo();
                  }}
                />
              ))}
            </ul>
          )}
        </Cartao>
      )}
    </div>
  );
}

function Emprestimo({ contrato, aoMudar }: { contrato: ContratoDaConta; aoMudar: () => void }) {
  const [erro, setErro] = useState<unknown>(null);
  const [ocupado, setOcupado] = useState(false);

  /**
   * Uma chave por parcela, guardada entre tentativas.
   *
   * Se o pagamento falhar e a pessoa clicar de novo, tem que ir a MESMA chave — senão o
   * crédito trata a segunda como outro pagamento tentando quitar a mesma parcela. Um
   * `useRef` porque a chave não desenha nada: trocá-la não pode redesenhar a tela, e
   * redesenhar a tela não pode trocá-la.
   */
  const chaves = useRef(new Map<number, string>());

  function chaveDaParcela(numero: number) {
    const existente = chaves.current.get(numero);
    if (existente) return existente;

    const nova = chaveNova();
    chaves.current.set(numero, nova);
    return nova;
  }

  async function agir(acao: () => Promise<unknown>) {
    setOcupado(true);
    setErro(null);

    try {
      await acao();
      aoMudar();
    } catch (falha) {
      setErro(falha);
    } finally {
      setOcupado(false);
    }
  }

  const pago = contrato.totalPago + contrato.saldoAberto;
  const progresso = pago > 0 ? (contrato.totalPago / pago) * 100 : 0;

  return (
    <li className="rounded-2xl border border-borda p-5">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex items-center gap-3">
          <span className="flex h-11 w-11 items-center justify-center rounded-xl bg-marca/8 text-marca">
            <Icone.Cedula />
          </span>
          <div>
            <p className="numero text-xl font-bold">{dinheiro(contrato.valorFinanciado)}</p>
            <p className="text-xs text-tinta-fraca">
              {contrato.prazoEmMeses}x · {contrato.sistema} ·{" "}
              {(contrato.taxaMensal * 100).toFixed(2).replace(".", ",")}% ao mês
            </p>
          </div>
        </div>
        <div className="text-right">
          <p className="text-xs text-tinta-fraca">Falta pagar</p>
          <p className="numero text-lg font-bold">{dinheiro(contrato.saldoAberto)}</p>
        </div>
      </div>

      <div className="mt-4 h-2 overflow-hidden rounded-full bg-papel">
        <div
          className="h-full rounded-full bg-entrada transition-[width] duration-500"
          style={{ width: `${progresso}%` }}
        />
      </div>
      <p className="mt-1.5 text-xs text-tinta-fraca">
        {contrato.parcelasPagas} de {contrato.prazoEmMeses} parcelas pagas
      </p>

      <div className="mt-4">
        <Aviso erro={erro} aoFechar={() => setErro(null)} />
      </div>

      {!contrato.desembolsadoEm ? (
        <div className="mt-4 flex flex-wrap items-center gap-3">
          <Botao disabled={ocupado} onClick={() => void agir(() => desembolsar(contrato.propostaId))}>
            Receber na conta
          </Botao>
          <span className="text-xs text-tinta-fraca">
            Contrato assinado e ainda não desembolsado.
          </span>
        </div>
      ) : contrato.estaQuitado ? (
        <p className="mt-4 flex items-center gap-2 rounded-xl bg-entrada/8 px-4 py-2.5 text-sm font-medium text-entrada">
          <Icone.Escudo className="h-4 w-4" /> Empréstimo quitado.
        </p>
      ) : (
        <div className="mt-4 flex flex-wrap items-center gap-3">
          <Botao
            disabled={ocupado}
            onClick={() =>
              void agir(() =>
                pagarParcela(
                  contrato.propostaId,
                  contrato.proxima.numero!,
                  chaveDaParcela(contrato.proxima.numero!),
                ),
              )
            }
          >
            Pagar parcela {contrato.proxima.numero} · {dinheiro(contrato.proxima.valor ?? 0)}
          </Botao>
          <span className="text-xs text-tinta-fraca">
            vence em {contrato.proxima.vencimento ? data(contrato.proxima.vencimento) : "—"} · sai
            desta conta
          </span>
        </div>
      )}
    </li>
  );
}

type Etapa = "pedido" | "decisao";

/**
 * O pedido inteiro, do formulário ao dinheiro na conta.
 *
 * São quatro chamadas ao crédito e cada uma pode falhar sozinha: cadastrar, analisar,
 * contratar e desembolsar. Elas ficam separadas de propósito — o desembolso, em especial,
 * é um passo à parte porque o dinheiro atravessa para outro serviço, e é o único que pode
 * ser repetido sem risco de creditar duas vezes.
 */
function PedirEmprestimo({
  sessao,
  aoFechar,
  aoConcluir,
}: {
  sessao: Sessao;
  aoFechar: () => void;
  aoConcluir: () => void;
}) {
  const [chave] = useState(chaveNova);
  const [etapa, setEtapa] = useState<Etapa>("pedido");
  const [erro, setErro] = useState<unknown>(null);
  const [ocupado, setOcupado] = useState(false);

  const [cpf, setCpf] = useState("");
  const [nascimento, setNascimento] = useState("");
  const [renda, setRenda] = useState("");
  const [valor, setValor] = useState("");
  const [prazo, setPrazo] = useState(12);
  const [sistema, setSistema] = useState<Sistema>("Price");

  const [propostaId, setPropostaId] = useState("");
  const [decisao, setDecisao] = useState<Decisao | null>(null);
  const [simulacao, setSimulacao] = useState<Simulacao | null>(null);

  const numeros = (texto: string) => Number(texto.replace(/\./g, "").replace(",", "."));
  const valido =
    cpf.replace(/\D/g, "").length === 11 &&
    nascimento.length === 10 &&
    numeros(renda) > 0 &&
    numeros(valor) > 0;

  async function enviar() {
    setOcupado(true);
    setErro(null);

    try {
      const proposta = await cadastrarProposta(
        {
          cpf: cpf.replace(/\D/g, ""),
          nomeSolicitante: sessao.titular,
          dataDeNascimento: nascimento,
          rendaMensal: numeros(renda),
          valorSolicitado: numeros(valor),
          prazoEmMeses: prazo,
          sistema,
        },
        chave,
      );

      setPropostaId(proposta.id);
      setSimulacao(await simular(proposta.id));
      setDecisao(await analisar(proposta.id));
      setEtapa("decisao");
    } catch (falha) {
      setErro(falha);
    } finally {
      setOcupado(false);
    }
  }

  async function aceitar() {
    setOcupado(true);
    setErro(null);

    try {
      await contratarNaConta(propostaId, sessao.contaId);
      await desembolsar(propostaId);
      aoConcluir();
    } catch (falha) {
      setErro(falha);
    } finally {
      setOcupado(false);
    }
  }

  return (
    <Cartao
      titulo={etapa === "pedido" ? "Pedir empréstimo" : "A resposta"}
      acao={
        <Botao variante="secundario" onClick={aoFechar}>
          Cancelar
        </Botao>
      }
    >
      <div className="space-y-4">
        <Aviso erro={erro} aoFechar={() => setErro(null)} />

        {etapa === "pedido" ? (
          <>
            <div className="grid gap-4 sm:grid-cols-2">
              <Campo rotulo="CPF" dica="Só números.">
                <input className={entrada} value={cpf} onChange={(e) => setCpf(e.target.value)} />
              </Campo>
              <Campo rotulo="Data de nascimento">
                <input
                  type="date"
                  className={entrada}
                  value={nascimento}
                  onChange={(e) => setNascimento(e.target.value)}
                />
              </Campo>
              <Campo rotulo="Renda mensal">
                <input
                  className={entrada}
                  inputMode="decimal"
                  value={renda}
                  onChange={(e) => setRenda(e.target.value)}
                  placeholder="7500"
                />
              </Campo>
              <Campo rotulo="Quanto você precisa">
                <input
                  className={entrada}
                  inputMode="decimal"
                  value={valor}
                  onChange={(e) => setValor(e.target.value)}
                  placeholder="10000"
                />
              </Campo>
              <Campo rotulo="Em quantas vezes">
                <select
                  className={entrada}
                  value={prazo}
                  onChange={(e) => setPrazo(Number(e.target.value))}
                >
                  {[6, 12, 18, 24, 36, 48].map((meses) => (
                    <option key={meses} value={meses}>
                      {meses}x
                    </option>
                  ))}
                </select>
              </Campo>
              <Campo rotulo="Sistema" dica="Price tem parcela fixa; SAC começa maior e diminui.">
                <select
                  className={entrada}
                  value={sistema}
                  onChange={(e) => setSistema(e.target.value as Sistema)}
                >
                  <option value="Price">Price</option>
                  <option value="Sac">SAC</option>
                </select>
              </Campo>
            </div>

            <Botao onClick={enviar} disabled={!valido || ocupado}>
              {ocupado ? "Analisando…" : "Pedir análise"}
            </Botao>
          </>
        ) : (
          <>
            <div
              className={`rounded-2xl px-5 py-4 ${
                decisao?.aprovada ? "bg-entrada/8" : "bg-amber-50"
              }`}
            >
              <strong
                className={`block text-lg font-bold ${
                  decisao?.aprovada ? "text-entrada" : "text-amber-900"
                }`}
              >
                {decisao?.aprovada ? "Aprovado" : "Não aprovado"}
              </strong>
              {decisao?.aprovada && simulacao && (
                <p className="numero mt-1 text-sm text-tinta">
                  {simulacao.prazoEmMeses}x de{" "}
                  <strong>{dinheiro(simulacao.primeiraParcela)}</strong> · total{" "}
                  {dinheiro(simulacao.totalPago)} · juros {dinheiro(simulacao.totalDeJuros)}
                </p>
              )}
            </div>

            <div>
              <p className="mb-2.5 text-xs font-semibold tracking-wide text-tinta-fraca uppercase">
                Por que a resposta foi essa
              </p>
              <ul className="space-y-2 text-sm">
                {decisao?.laudo.map((linha) => (
                  <li key={linha.codigo} className="flex gap-2.5">
                    <span
                      className={`mt-0.5 flex h-4 w-4 shrink-0 items-center justify-center rounded-full text-[10px] font-bold text-white ${
                        linha.aprovou ? "bg-entrada" : "bg-saida"
                      }`}
                    >
                      {linha.aprovou ? "✓" : "✕"}
                    </span>
                    <span className="text-tinta-fraca">{linha.motivo}</span>
                  </li>
                ))}
              </ul>
            </div>

            {decisao?.aprovada ? (
              <Botao onClick={aceitar} disabled={ocupado}>
                {ocupado ? "Contratando…" : "Aceitar e receber na conta"}
              </Botao>
            ) : (
              <Botao variante="secundario" onClick={aoFechar}>
                Fechar
              </Botao>
            )}
          </>
        )}
      </div>
    </Cartao>
  );
}
