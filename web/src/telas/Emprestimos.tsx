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
import { Aviso, Botao, Campo, Cartao, Carregando, Vazio, entrada } from "../componentes/base";
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
            <Vazio>Você não tem empréstimo nesta conta.</Vazio>
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
    <li className="rounded-lg border border-borda p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="numero text-lg font-semibold">{dinheiro(contrato.valorFinanciado)}</p>
          <p className="text-xs text-tinta/55">
            {contrato.prazoEmMeses}x · {contrato.sistema} ·{" "}
            {(contrato.taxaMensal * 100).toFixed(2).replace(".", ",")}% ao mês
          </p>
        </div>
        <div className="text-right">
          <p className="text-xs text-tinta/50">Falta pagar</p>
          <p className="numero font-semibold">{dinheiro(contrato.saldoAberto)}</p>
        </div>
      </div>

      <div className="mt-3 h-1.5 overflow-hidden rounded-full bg-tinta/10">
        <div className="h-full rounded-full bg-credito" style={{ width: `${progresso}%` }} />
      </div>
      <p className="mt-1 text-xs text-tinta/50">
        {contrato.parcelasPagas} de {contrato.prazoEmMeses} parcelas pagas
      </p>

      <div className="mt-4">
        <Aviso erro={erro} aoFechar={() => setErro(null)} />
      </div>

      {!contrato.desembolsadoEm ? (
        <div className="mt-4 flex flex-wrap items-center gap-3">
          <Botao disabled={ocupado} onClick={() => void agir(() => desembolsar(contrato.propostaId))}>
            Receber o dinheiro na conta
          </Botao>
          <span className="text-xs text-tinta/45">
            Contrato assinado e ainda não desembolsado.
          </span>
        </div>
      ) : contrato.estaQuitado ? (
        <p className="mt-4 rounded-lg bg-credito/10 px-4 py-2 text-sm text-credito">
          Empréstimo quitado.
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
          <span className="text-xs text-tinta/45">
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
              className={`rounded-lg px-4 py-3 ${
                decisao?.aprovada ? "bg-credito/10 text-credito" : "bg-amber-50 text-amber-900"
              }`}
            >
              <strong className="block">
                {decisao?.aprovada ? "Aprovado" : "Não aprovado"}
              </strong>
              {decisao?.aprovada && simulacao && (
                <span className="text-sm text-tinta/70">
                  {simulacao.prazoEmMeses}x de {dinheiro(simulacao.primeiraParcela)} · total de{" "}
                  {dinheiro(simulacao.totalPago)} · juros de {dinheiro(simulacao.totalDeJuros)}
                </span>
              )}
            </div>

            <div>
              <p className="mb-2 text-xs font-medium text-tinta/60">
                Por que a resposta foi essa
              </p>
              <ul className="space-y-1.5 text-sm">
                {decisao?.laudo.map((linha) => (
                  <li key={linha.codigo} className="flex gap-2">
                    <span className={linha.aprovou ? "text-credito" : "text-debito"}>
                      {linha.aprovou ? "✓" : "✕"}
                    </span>
                    <span className="text-tinta/70">{linha.motivo}</span>
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
