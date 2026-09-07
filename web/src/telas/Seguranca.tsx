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
import { Aviso, Botao, Campo, Cartao, Carregando, Vazio, entrada } from "../componentes/base";
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
      <Cartao titulo="Estado da conta">
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

          <p className="text-xs text-tinta/45">
            Encerrar exige saldo zero. Conta encerrada é terminal: não volta a ser ativa, e
            quem quiser conta de novo abre outra — as duas histórias ficam separadas.
          </p>
        </div>
      </Cartao>

      <Cartao titulo="Conciliação do ledger">
        {conciliacao === null ? (
          <Carregando />
        ) : (
          <div className="space-y-3">
            <div
              className={`rounded-lg px-4 py-3 text-sm ${
                conciliacao.bate ? "bg-credito/10 text-credito" : "bg-debito/10 text-debito"
              }`}
            >
              <strong>
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

            <p className="text-xs text-tinta/45">
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
          <ol className="space-y-3">
            {historico.map((mudanca) => (
              <li key={mudanca.sequencia} className="flex gap-3 text-sm">
                <span className="numero shrink-0 text-xs text-tinta/40">
                  {dataHora(mudanca.ocorridaEm)}
                </span>
                <div>
                  <p>
                    {mudanca.de} → <strong>{mudanca.para}</strong>
                  </p>
                  <p className="text-tinta/60">{mudanca.motivo}</p>
                  <p className="text-xs text-tinta/40">por {mudanca.origem}</p>
                </div>
              </li>
            ))}
          </ol>
        )}
      </Cartao>
    </div>
  );
}

const Numero = ({ rotulo, valor }: { rotulo: string; valor: string }) => (
  <div>
    <dt className="text-xs text-tinta/50">{rotulo}</dt>
    <dd className="numero font-medium">{valor}</dd>
  </div>
);
