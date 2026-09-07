import { useState } from "react";
import { abrirConta, conta as buscarConta } from "../api/banco";
import { Aviso, Botao, Campo, entrada } from "../componentes/base";
import { Marca } from "../componentes/marca";
import type { Sessao } from "../sessao";

/**
 * A porta de entrada — e não uma tela de login.
 *
 * Não há autenticação nas APIs: qualquer pessoa com o id de uma conta entra nela. Isso
 * está registrado no README, entre as pendências, e não na tela — mas continua valendo, e
 * é o motivo de não existir campo de senha aqui: um que não validasse nada seria pior do
 * que nenhum.
 */
export default function Entrada({ aoEntrar }: { aoEntrar: (sessao: Sessao) => void }) {
  const [modo, setModo] = useState<"entrar" | "abrir">("entrar");
  const [titular, setTitular] = useState("");
  const [contaId, setContaId] = useState("");
  const [erro, setErro] = useState<unknown>(null);
  const [ocupado, setOcupado] = useState(false);

  async function tentar(acao: () => Promise<Sessao>) {
    setOcupado(true);
    setErro(null);

    try {
      aoEntrar(await acao());
    } catch (falha) {
      setErro(falha);
    } finally {
      setOcupado(false);
    }
  }

  const abrir = () =>
    tentar(async () => {
      const nova = await abrirConta(titular.trim());
      return { contaId: nova.id, titular: nova.titular, numero: nova.numero };
    });

  const entrar = () =>
    tentar(async () => {
      const encontrada = await buscarConta(contaId.trim());
      return { contaId: encontrada.id, titular: encontrada.titular, numero: encontrada.numero };
    });

  return (
    <div className="min-h-screen lg:grid lg:grid-cols-2">
      {/* Lado da marca: só existe em tela grande, onde há espaço sobrando. */}
      <div className="cartao-marca relative hidden overflow-hidden p-12 text-white lg:flex lg:flex-col lg:justify-between">
        <div className="pointer-events-none absolute -top-24 -right-24 h-96 w-96 rounded-full bg-white/10" />
        <div className="pointer-events-none absolute -bottom-32 -left-20 h-96 w-96 rounded-full bg-white/5" />

        <div className="relative flex items-center gap-3">
          <Marca tamanho={40} />
          <span className="text-lg font-bold">Plataforma Bancária</span>
        </div>

        <div className="relative max-w-sm">
          <h2 className="text-3xl leading-tight font-bold">
            Conta, extrato e empréstimo no mesmo lugar.
          </h2>
          <p className="mt-4 text-white/70">
            Cada movimentação é um lançamento imutável no ledger, e o saldo se prova contra
            ele a qualquer momento.
          </p>
        </div>

        <p className="relative text-sm text-white/50">
          Uma fatia de core banking, construída para ser auditável.
        </p>
      </div>

      <div className="flex min-h-screen flex-col justify-center p-6 sm:p-12">
        <div className="mx-auto w-full max-w-sm">
          <div className="mb-8 flex items-center gap-3 text-marca lg:hidden">
            <Marca tamanho={40} />
            <span className="text-lg font-bold text-tinta">Plataforma Bancária</span>
          </div>

          <h1 className="text-2xl font-bold">
            {modo === "entrar" ? "Entrar na sua conta" : "Abrir uma conta"}
          </h1>
          <p className="mt-1.5 text-sm text-tinta-fraca">
            {modo === "entrar"
              ? "Informe o identificador da conta."
              : "A conta nasce zerada e ativa."}
          </p>

          <div className="mt-7 space-y-4">
            <Aviso erro={erro} aoFechar={() => setErro(null)} />

            {modo === "entrar" ? (
              <>
                <Campo rotulo="Identificador da conta">
                  <input
                    className={entrada}
                    value={contaId}
                    onChange={(e) => setContaId(e.target.value)}
                    onKeyDown={(e) => e.key === "Enter" && contaId.trim().length >= 10 && entrar()}
                    placeholder="01a07dc4-94b4-730d-…"
                    autoFocus
                  />
                </Campo>
                <Botao className="w-full" onClick={entrar} disabled={ocupado || contaId.trim().length < 10}>
                  {ocupado ? "Entrando…" : "Entrar"}
                </Botao>
              </>
            ) : (
              <>
                <Campo rotulo="Nome do titular">
                  <input
                    className={entrada}
                    value={titular}
                    onChange={(e) => setTitular(e.target.value)}
                    onKeyDown={(e) => e.key === "Enter" && titular.trim().length >= 2 && abrir()}
                    placeholder="Ana Ribeiro"
                    autoFocus
                  />
                </Campo>
                <Botao className="w-full" onClick={abrir} disabled={ocupado || titular.trim().length < 2}>
                  {ocupado ? "Abrindo…" : "Abrir conta"}
                </Botao>
              </>
            )}
          </div>

          <p className="mt-6 text-center text-sm text-tinta-fraca">
            {modo === "entrar" ? "Ainda não tem conta?" : "Já tem uma conta?"}{" "}
            <button
              onClick={() => {
                setModo(modo === "entrar" ? "abrir" : "entrar");
                setErro(null);
              }}
              className="font-semibold text-marca hover:underline"
            >
              {modo === "entrar" ? "Abrir agora" : "Entrar"}
            </button>
          </p>
        </div>
      </div>
    </div>
  );
}
