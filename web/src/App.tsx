import { useCallback, useEffect, useState } from "react";
import { conta as buscarConta, type Conta } from "./api/banco";
import { Aviso, Carregando } from "./componentes/base";
import * as Icone from "./componentes/icones";
import { Marca } from "./componentes/marca";
import * as sessaoGuardada from "./sessao";
import type { Sessao } from "./sessao";
import Emprestimos from "./telas/Emprestimos";
import Entrada from "./telas/Entrada";
import Extrato from "./telas/Extrato";
import Painel from "./telas/Painel";
import Seguranca from "./telas/Seguranca";

const ABAS = [
  { nome: "Conta", Icone: Icone.Casa },
  { nome: "Extrato", Icone: Icone.Lista },
  { nome: "Empréstimos", Icone: Icone.Cedula },
  { nome: "Segurança", Icone: Icone.Escudo },
] as const;

type Aba = (typeof ABAS)[number]["nome"];

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

  const iniciais = sessao.titular
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((parte) => parte[0])
    .join("")
    .toUpperCase();

  return (
    <div className="min-h-screen lg:flex">
      {/* Coluna fixa no desktop; no celular ela some e a navegação vai para o rodapé. */}
      <aside className="hidden w-64 shrink-0 flex-col justify-between border-r border-borda bg-white p-6 lg:sticky lg:top-0 lg:flex lg:h-screen">
        <div>
          <div className="flex items-center gap-3 text-marca">
            <Marca tamanho={38} />
            <div className="leading-tight">
              <p className="text-sm font-bold text-tinta">Plataforma</p>
              <p className="text-sm font-bold text-tinta">Bancária</p>
            </div>
          </div>

          <nav className="mt-9 space-y-1">
            {ABAS.map(({ nome, Icone: Desenho }) => (
              <button
                key={nome}
                onClick={() => setAba(nome)}
                className={`flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition ${
                  aba === nome
                    ? "bg-marca/8 text-marca"
                    : "text-tinta-fraca hover:bg-papel hover:text-tinta"
                }`}
              >
                <Desenho />
                {nome}
              </button>
            ))}
          </nav>
        </div>

        <div>
          <div className="mb-3 flex items-center gap-3 rounded-xl bg-papel px-3 py-2.5">
            <span className="flex h-9 w-9 items-center justify-center rounded-full bg-marca text-xs font-bold text-white">
              {iniciais}
            </span>
            <div className="min-w-0">
              <p className="truncate text-sm font-semibold">{sessao.titular}</p>
              <p className="numero text-xs text-tinta-fraca">{sessao.numero}</p>
            </div>
          </div>
          <button
            onClick={sair}
            className="flex w-full items-center gap-3 rounded-xl px-3 py-2 text-sm text-tinta-fraca transition hover:text-saida"
          >
            <Icone.Sair />
            Sair
          </button>
        </div>
      </aside>

      <div className="min-w-0 flex-1">
        <header className="flex items-center justify-between border-b border-borda bg-white px-5 py-4 lg:hidden">
          <div className="flex items-center gap-2.5 text-marca">
            <Marca tamanho={30} />
            <span className="text-sm font-bold text-tinta">Plataforma Bancária</span>
          </div>
          <button onClick={sair} className="text-tinta-fraca">
            <Icone.Sair />
          </button>
        </header>

        <main className="mx-auto max-w-3xl space-y-5 p-5 pb-28 sm:p-8 lg:pb-10">
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

      <nav className="fixed inset-x-0 bottom-0 z-10 flex border-t border-borda bg-white/95 backdrop-blur lg:hidden">
        {ABAS.map(({ nome, Icone: Desenho }) => (
          <button
            key={nome}
            onClick={() => setAba(nome)}
            className={`flex flex-1 flex-col items-center gap-1 py-3 text-[11px] font-medium transition ${
              aba === nome ? "text-marca" : "text-tinta-fraca"
            }`}
          >
            <Desenho />
            {nome}
          </button>
        ))}
      </nav>
    </div>
  );
}
