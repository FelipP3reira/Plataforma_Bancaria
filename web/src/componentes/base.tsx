import type { ReactNode } from "react";
import { ErroDaApi, dinheiro } from "../api/cliente";

export function Cartao({ titulo, acao, children }: { titulo?: string; acao?: ReactNode; children: ReactNode }) {
  return (
    <section className="rounded-xl border border-borda bg-white p-5 shadow-sm">
      {titulo && (
        <header className="mb-4 flex items-center justify-between gap-3">
          <h2 className="text-sm font-semibold tracking-wide text-tinta/70 uppercase">{titulo}</h2>
          {acao}
        </header>
      )}
      {children}
    </section>
  );
}

export function Botao({
  children,
  variante = "principal",
  ...resto
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { variante?: "principal" | "secundario" | "perigo" }) {
  const estilos = {
    principal: "bg-marca text-white hover:brightness-110",
    secundario: "border border-borda bg-white hover:bg-papel",
    perigo: "border border-debito/40 bg-white text-debito hover:bg-debito/5",
  } as const;

  return (
    <button
      {...resto}
      className={`rounded-lg px-4 py-2 text-sm font-medium transition disabled:cursor-not-allowed disabled:opacity-40 ${estilos[variante]}`}
    >
      {children}
    </button>
  );
}

export function Campo({
  rotulo,
  dica,
  children,
}: {
  rotulo: string;
  dica?: string;
  children: ReactNode;
}) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-tinta/60">{rotulo}</span>
      {children}
      {dica && <span className="mt-1 block text-xs text-tinta/45">{dica}</span>}
    </label>
  );
}

export const entrada =
  "w-full rounded-lg border border-borda bg-white px-3 py-2 text-sm outline-none focus:border-marca";

/**
 * O aviso de recusa.
 *
 * Separa recusa de falha porque as duas pedem ações diferentes de quem está na tela: uma
 * pede corrigir o pedido, a outra pede tentar de novo. Mostrar as duas com a mesma cara
 * faria a pessoa insistir num pedido que nunca vai passar.
 */
export function Aviso({ erro, aoFechar }: { erro: unknown; aoFechar?: () => void }) {
  if (!erro) return null;

  const daApi = erro instanceof ErroDaApi ? erro : null;
  const recusa = daApi?.ehRecusa ?? false;

  return (
    <div
      className={`flex items-start justify-between gap-3 rounded-lg border px-4 py-3 text-sm ${
        recusa ? "border-amber-300 bg-amber-50 text-amber-900" : "border-debito/30 bg-debito/5 text-debito"
      }`}
    >
      <div>
        <strong className="block">{daApi?.titulo ?? "Algo deu errado"}</strong>
        <span className="text-tinta/70">
          {daApi?.detalhe ?? (erro instanceof Error ? erro.message : String(erro))}
        </span>
      </div>
      {aoFechar && (
        <button onClick={aoFechar} className="shrink-0 text-xs underline">
          fechar
        </button>
      )}
    </div>
  );
}

export const Vazio = ({ children }: { children: ReactNode }) => (
  <p className="py-8 text-center text-sm text-tinta/45">{children}</p>
);

export const Carregando = () => <Vazio>Carregando…</Vazio>;

/** Valor com sinal e cor. O sinal vem da API, e não de uma conta feita aqui. */
export function Valor({ efeito }: { efeito: number }) {
  return (
    <span className={`numero font-medium ${efeito < 0 ? "text-debito" : "text-credito"}`}>
      {efeito > 0 ? "+" : ""}
      {dinheiro(efeito)}
    </span>
  );
}

export function Selo({ estado }: { estado: string }) {
  const cores: Record<string, string> = {
    Ativa: "bg-credito/10 text-credito",
    Bloqueada: "bg-amber-100 text-amber-800",
    Encerrada: "bg-tinta/10 text-tinta/60",
  };

  return (
    <span className={`rounded-full px-2.5 py-1 text-xs font-medium ${cores[estado] ?? "bg-tinta/10"}`}>
      {estado}
    </span>
  );
}
