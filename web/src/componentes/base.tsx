import type { ReactNode } from "react";
import { ErroDaApi, dinheiro } from "../api/cliente";

export function Cartao({
  titulo,
  acao,
  children,
  className = "",
}: {
  titulo?: string;
  acao?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={`surgir rounded-2xl border border-borda bg-white p-5 sm:p-6 ${className}`}>
      {titulo && (
        <header className="mb-5 flex items-center justify-between gap-3">
          <h2 className="text-base font-semibold">{titulo}</h2>
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
  className = "",
  ...resto
}: React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variante?: "principal" | "secundario" | "perigo";
}) {
  const estilos = {
    principal: "bg-marca text-white hover:bg-marca-clara active:scale-[0.98]",
    secundario: "border border-borda bg-white text-tinta hover:border-marca/40 hover:text-marca",
    perigo: "border border-saida/30 bg-white text-saida hover:bg-saida/5",
  } as const;

  return (
    <button
      {...resto}
      className={`rounded-xl px-5 py-2.5 text-sm font-semibold transition disabled:pointer-events-none disabled:opacity-40 ${estilos[variante]} ${className}`}
    >
      {children}
    </button>
  );
}

/** Ação em círculo, como a fileira de atalhos de um app de banco. */
export function Atalho({
  icone,
  rotulo,
  ativo,
  ...resto
}: React.ButtonHTMLAttributes<HTMLButtonElement> & {
  icone: ReactNode;
  rotulo: string;
  ativo?: boolean;
}) {
  return (
    <button {...resto} className="group flex w-20 flex-col items-center gap-2 disabled:opacity-35">
      <span
        className={`flex h-14 w-14 items-center justify-center rounded-2xl border transition ${
          ativo
            ? "border-marca bg-marca text-white"
            : "border-borda bg-white text-marca group-hover:border-marca/40 group-enabled:group-hover:-translate-y-0.5"
        }`}
      >
        {icone}
      </span>
      <span className="text-xs font-medium text-tinta-fraca">{rotulo}</span>
    </button>
  );
}

export function Campo({ rotulo, dica, children }: { rotulo: string; dica?: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-xs font-semibold text-tinta-fraca">{rotulo}</span>
      {children}
      {dica && <span className="mt-1.5 block text-xs text-tinta-fraca/75">{dica}</span>}
    </label>
  );
}

export const entrada =
  "w-full rounded-xl border border-borda bg-white px-4 py-2.5 text-sm outline-none transition placeholder:text-tinta-fraca/50 focus:border-marca focus:ring-4 focus:ring-marca/10";

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
      className={`surgir flex items-start justify-between gap-3 rounded-xl border px-4 py-3 text-sm ${
        recusa
          ? "border-amber-200 bg-amber-50 text-amber-900"
          : "border-saida/25 bg-saida/5 text-saida"
      }`}
    >
      <div>
        <strong className="block font-semibold">{daApi?.titulo ?? "Algo deu errado"}</strong>
        <span className="opacity-80">
          {daApi?.detalhe ?? (erro instanceof Error ? erro.message : String(erro))}
        </span>
      </div>
      {aoFechar && (
        <button onClick={aoFechar} className="shrink-0 text-xs font-medium underline opacity-70">
          fechar
        </button>
      )}
    </div>
  );
}

export const Vazio = ({ children }: { children: ReactNode }) => (
  <p className="py-10 text-center text-sm text-tinta-fraca">{children}</p>
);

export const Carregando = () => (
  <div className="flex items-center justify-center gap-2 py-10 text-sm text-tinta-fraca">
    <span className="h-2 w-2 animate-bounce rounded-full bg-marca [animation-delay:-0.2s]" />
    <span className="h-2 w-2 animate-bounce rounded-full bg-marca [animation-delay:-0.1s]" />
    <span className="h-2 w-2 animate-bounce rounded-full bg-marca" />
  </div>
);

/** Valor com sinal e cor. O sinal vem da API, e não de uma conta feita aqui. */
export function Valor({ efeito }: { efeito: number }) {
  return (
    <span className={`numero font-semibold ${efeito < 0 ? "text-saida" : "text-entrada"}`}>
      {efeito > 0 ? "+" : "−"}
      {dinheiro(Math.abs(efeito)).replace("R$", "R$ ").replace(/\s+/g, " ")}
    </span>
  );
}

export function Selo({ estado }: { estado: string }) {
  const cores: Record<string, string> = {
    Ativa: "bg-entrada/10 text-entrada",
    Bloqueada: "bg-amber-100 text-amber-800",
    Encerrada: "bg-tinta/10 text-tinta-fraca",
  };

  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-semibold ${cores[estado] ?? "bg-tinta/10"}`}
    >
      <span className="h-1.5 w-1.5 rounded-full bg-current" />
      {estado}
    </span>
  );
}

/**
 * O disco com o ícone que abre cada linha do extrato.
 *
 * A cor sai do sinal do lançamento, e não do texto da descrição: é o mesmo dado que decide
 * o sinal do valor à direita, então as duas pontas da linha nunca se contradizem.
 */
export function Disco({ efeito, children }: { efeito: number; children: ReactNode }) {
  return (
    <span
      className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full ${
        efeito < 0 ? "bg-saida/10 text-saida" : "bg-entrada/10 text-entrada"
      }`}
    >
      {children}
    </span>
  );
}
