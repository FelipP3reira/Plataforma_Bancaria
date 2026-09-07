import { useCallback, useEffect, useState } from "react";
import { conta as buscarConta, type Conta } from "./api/banco";
import { Aviso, Carregando } from "./componentes/base";
import * as sessaoGuardada from "./sessao";
import type { Sessao } from "./sessao";
import Emprestimos from "./telas/Emprestimos";
import Entrada from "./telas/Entrada";
import Extrato from "./telas/Extrato";
import Painel from "./telas/Painel";
import Seguranca from "./telas/Seguranca";

const ABAS = ["Conta", "Extrato", "Empréstimos", "Segurança"] as const;
type Aba = (typeof ABAS)[number];

export default function App() {
  const [sessao, setSessao] = useState<Sessao | null>(sessaoGuardada.ler);
  const [conta, setConta] = useState<Conta | null>(null);
  const [aba, setAba] = useState<Aba>("Conta");
  const [erro, setErro] = useState<unknown>(null);

  /**
   * Relê a conta do servidor.
   *
   * Toda tela que move dinheiro chama isto no fim, em vez de ajustar o saldo na memória.
   * Saldo calculado no cliente diverge do ledger no primeiro caso que a interface não
   * previu — e o saldo é justamente o número que não pode estar errado nesta tela.
   */
  const recarregar = useCallback(async () => {
    if (!sessao) return;

    try {
      setConta(await buscarConta(sessao.contaId));
      setErro(null);
    } catch (falha) {
      setErro(falha);
    }
  }, [sessao]);

  useEffect(() => {
    void recarregar();
  }, [recarregar]);

  function entrar(nova: Sessao) {
    sessaoGuardada.gravar(nova);
    setSessao(nova);
    setAba("Conta");
  }

  function sair() {
    sessaoGuardada.limpar();
    setSessao(null);
    setConta(null);
  }

  if (!sessao) return <Entrada aoEntrar={entrar} />;

  return (
    <div className="min-h-screen">
      <header className="border-b border-borda bg-white">
        <div className="mx-auto flex max-w-4xl flex-wrap items-center justify-between gap-3 px-6 py-4">
          <div>
            <h1 className="font-semibold">Plataforma Bancária</h1>
            <p className="text-xs text-tinta/50">
              {sessao.titular} · conta {sessao.numero}
            </p>
          </div>
          <button onClick={sair} className="text-sm text-tinta/60 underline">
            Sair
          </button>
        </div>

        <nav className="mx-auto flex max-w-4xl gap-1 px-6">
          {ABAS.map((nome) => (
            <button
              key={nome}
              onClick={() => setAba(nome)}
              className={`-mb-px border-b-2 px-3 py-2 text-sm transition ${
                aba === nome
                  ? "border-marca font-medium text-marca"
                  : "border-transparent text-tinta/55 hover:text-tinta"
              }`}
            >
              {nome}
            </button>
          ))}
        </nav>
      </header>

      <main className="mx-auto max-w-4xl space-y-6 p-6">
        <Aviso erro={erro} aoFechar={() => setErro(null)} />

        {!conta ? (
          <Carregando />
        ) : aba === "Conta" ? (
          <Painel conta={conta} sessao={sessao} aoMudar={recarregar} />
        ) : aba === "Extrato" ? (
          <Extrato contaId={conta.id} />
        ) : aba === "Empréstimos" ? (
          <Emprestimos sessao={sessao} aoMudarSaldo={recarregar} />
        ) : (
          <Seguranca conta={conta} sessao={sessao} aoMudar={recarregar} />
        )}
      </main>
    </div>
  );
}
