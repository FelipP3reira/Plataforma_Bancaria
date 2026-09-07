import { useCallback, useEffect, useState } from "react";
import {
  bloquear,
  conciliacao as buscarConciliacao,
  desbloquear,
  encerrar,
  historicoDeEstado,
  type Conciliacao,
  type Conta,
  type MudancaDeEstado,
} from "../api/banco";
import { dataHora, dinheiro } from "../api/cliente";
import { Aviso, Botao, Campo, Cartao, Carregando, Selo, Vazio, entrada } from "../componentes/base";
import * as Icone from "../componentes/icones";
import { operadorDe, type Sessao } from "../sessao";

export default function Seguranca({
  conta,
  sessao,
  aoMudar,
}: {
  conta: Conta;
  sessao: Sessao;
  aoMudar: () => void;
}) {
  const [motivo, setMotivo] = useState("");
  const [historico, setHistorico] = useState<MudancaDeEstado[] | null>(null);
  const [conciliacao, setConciliacao] = useState<Conciliacao | null>(null);
  const [erro, setErro] = useState<unknown>(null);
  const [ocupado, setOcupado] = useState(false);

  const recarregar = useCallback(async () => {
    try {
      const [trilha, conferencia] = await Promise.all([
        historicoDeEstado(conta.id),
        buscarConciliacao(conta.id),
      ]);

      setHistorico(trilha);
      setConciliacao(conferencia);
    } catch (falha) {
      setErro(falha);
      setHistorico([]);
    }
  }, [conta.id]);

  useEffect(() => {
    void recarregar();
  }, [recarregar]);

  async function agir(acao: (motivo: string, operador: string) => Promise<unknown>) {
    setOcupado(true);
    setErro(null);

    try {
      await acao(motivo.trim(), operadorDe(sessao));
      setMotivo("");
      await recarregar();
      aoMudar();
    } catch (falha) {
      setErro(falha);
    } finally {
      setOcupado(false);
    }
  }

  const semMotivo = motivo.trim().length === 0;

  return (
    <div className="space-y-6">
      <Cartao titulo="Estado da conta" acao={<Selo estado={conta.estado} />}>
        <div className="space-y-4">
          <Aviso erro={erro} aoFechar={() => setErro(null)} />

          <Campo
            rotulo="Motivo"
            dica="Obrigatório e gravado na trilha. Bloqueio sem motivo não se audita."
          >
            <input
              className={entrada}
              value={motivo}
              onChange={(e) => setMotivo(e.target.value)}
              placeholder="suspeita de fraude"
              maxLength={200}
            />
          </Campo>

          <div className="flex flex-wrap gap-2">
            <Botao
              variante="perigo"
              disabled={semMotivo || ocupado || conta.estado !== "Ativa"}
              onClick={() => void agir((m, o) => bloquear(conta.id, m, o))}
            >
              Bloquear
            </Botao>
            <Botao
              variante="secundario"
              disabled={semMotivo || ocupado || conta.estado !== "Bloqueada"}
              onClick={() => void agir((m, o) => desbloquear(conta.id, m, o))}
            >
              Desbloquear
            </Botao>
            <Botao
              variante="perigo"
              disabled={semMotivo || ocupado || conta.estado === "Encerrada"}
              onClick={() => void agir((m, o) => encerrar(conta.id, m, o))}
            >
              Encerrar
            </Botao>
          </div>

          <p className="flex items-start gap-2 text-xs text-tinta-fraca">
            <Icone.Cadeado className="mt-0.5 h-4 w-4 shrink-0" />
            <span>
              Encerrar exige saldo zero. Conta encerrada é terminal: não volta a ser ativa,
              e quem quiser conta de novo abre outra — as duas histórias ficam separadas.
            </span>
          </p>
        </div>
      </Cartao>

      <Cartao titulo="Conciliação do ledger">
        {conciliacao === null ? (
          <Carregando />
        ) : (
          <div className="space-y-3">
            <div
              className={`flex items-center gap-3 rounded-2xl px-5 py-4 ${
                conciliacao.bate ? "bg-entrada/8 text-entrada" : "bg-saida/8 text-saida"
              }`}
            >
              <Icone.Escudo className="h-6 w-6 shrink-0" />
              <strong className="font-semibold">
                {conciliacao.bate
                  ? "O saldo bate com o ledger."
                  : "O saldo NÃO bate com o ledger."}
              </strong>
            </div>

            <dl className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm sm:grid-cols-4">
              <Numero rotulo="Saldo em cache" valor={dinheiro(conciliacao.saldoMaterializado)} />
              <Numero rotulo="Soma do ledger" valor={dinheiro(conciliacao.somaDoLedger)} />
              <Numero rotulo="Lançamentos" valor={String(conciliacao.lancamentos)} />
              <Numero rotulo="Quebras na corrente" valor={String(conciliacao.quebrasNaCorrente)} />
            </dl>

            <p className="text-xs text-tinta-fraca">
              O saldo da conta é um cache. Esta tela recalcula a soma do ledger e confere se
              cada lançamento continua de onde o anterior parou — é a conferência que prova
              que nenhum centavo foi criado nem perdido.
            </p>
          </div>
        )}
      </Cartao>

      <Cartao titulo="Trilha de estados">
        {historico === null ? (
          <Carregando />
        ) : historico.length === 0 ? (
          <Vazio>A conta nunca mudou de estado.</Vazio>
        ) : (
          <ol className="relative space-y-5 border-l border-borda pl-6">
            {historico.map((mudanca) => (
              <li key={mudanca.sequencia} className="relative text-sm">
                <span className="absolute top-1.5 -left-[1.72rem] h-2.5 w-2.5 rounded-full bg-marca ring-4 ring-white" />
                <p className="font-medium">
                  {mudanca.de} <span className="text-tinta-fraca">→</span>{" "}
                  <strong>{mudanca.para}</strong>
                </p>
                <p className="text-tinta-fraca">{mudanca.motivo}</p>
                <p className="numero mt-0.5 text-xs text-tinta-fraca/75">
                  {dataHora(mudanca.ocorridaEm)} · por {mudanca.origem}
                </p>
              </li>
            ))}
          </ol>
        )}
      </Cartao>
    </div>
  );
}

const Numero = ({ rotulo, valor }: { rotulo: string; valor: string }) => (
  <div className="rounded-xl bg-papel px-4 py-3">
    <dt className="text-xs text-tinta-fraca">{rotulo}</dt>
    <dd className="numero mt-0.5 font-semibold">{valor}</dd>
  </div>
);
