/**
 * A marca do produto.
 *
 * Monograma, e não um nome de banco inventado: o projeto se chama pelo que é. Uma marca
 * fictícia daria cara de produto de verdade, mas ao custo de parecer uma instituição que
 * não existe — e isto é um portfólio, não um banco.
 */
export function Marca({ tamanho = 36 }: { tamanho?: number }) {
  return (
    <svg width={tamanho} height={tamanho} viewBox="0 0 32 32" aria-label="Plataforma Bancária">
      <rect width="32" height="32" rx="9" fill="currentColor" />
      <path
        d="M9 23V9h5a3.5 3.5 0 010 7H9m0 0h5.6a3.5 3.5 0 010 7H9"
        stroke="white"
        strokeWidth="2.3"
        fill="none"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}
