import { useState } from "react";
import { abrirConta, conta as buscarConta } from "../api/banco";
import { Aviso, Botao, Campo, entrada } from "../componentes/base";
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
    <main className="mx-auto flex min-h-screen max-w-md flex-col justify-center gap-6 p-6">
      <header>
        <h1 className="text-2xl font-semibold">Plataforma Bancária</h1>
        <p className="mt-1 text-sm text-tinta/60">
          Contas, extrato, transferência e empréstimo.
        </p>
      </header>

      <Aviso erro={erro} aoFechar={() => setErro(null)} />

      <div className="space-y-4 rounded-xl border border-borda bg-white p-5">
        <h2 className="text-sm font-semibold">Abrir uma conta</h2>
        <Campo rotulo="Nome do titular">
          <input
            className={entrada}
            value={titular}
            onChange={(e) => setTitular(e.target.value)}
            placeholder="Ana Ribeiro"
          />
        </Campo>
        <Botao onClick={abrir} disabled={ocupado || titular.trim().length < 2}>
          Abrir conta
        </Botao>
      </div>

      <div className="space-y-4 rounded-xl border border-borda bg-white p-5">
        <h2 className="text-sm font-semibold">Entrar numa conta que já existe</h2>
        <Campo rotulo="Id da conta" dica="O identificador devolvido quando a conta foi aberta.">
          <input
            className={entrada}
            value={contaId}
            onChange={(e) => setContaId(e.target.value)}
            placeholder="0199…"
          />
        </Campo>
        <Botao variante="secundario" onClick={entrar} disabled={ocupado || contaId.trim().length < 10}>
          Entrar
        </Botao>
      </div>
    </main>
  );
}
