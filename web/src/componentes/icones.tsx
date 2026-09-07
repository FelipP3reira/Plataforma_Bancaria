/**
 * Ícones desenhados aqui, e não trazidos de uma biblioteca.
 *
 * São nove traços simples; uma dependência inteira para isso custaria mais em peso e em
 * superfície de atualização do que custa mantê-los. Todos herdam `currentColor` e o
 * tamanho vem do CSS, para que a cor de um ícone seja a cor do texto ao lado dele.
 */
type Props = { className?: string };

const base = "h-5 w-5";

const Svg = ({ children, className }: Props & { children: React.ReactNode }) => (
  <svg
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth={1.8}
    strokeLinecap="round"
    strokeLinejoin="round"
    className={className ?? base}
    aria-hidden="true"
  >
    {children}
  </svg>
);

export const Entrada = (p: Props) => (
  <Svg {...p}>
    <path d="M12 5v14M6 13l6 6 6-6" />
  </Svg>
);

export const Saida = (p: Props) => (
  <Svg {...p}>
    <path d="M12 19V5M6 11l6-6 6 6" />
  </Svg>
);

export const Troca = (p: Props) => (
  <Svg {...p}>
    <path d="M7 4L3 8l4 4M3 8h13M17 20l4-4-4-4M21 16H8" />
  </Svg>
);

export const Casa = (p: Props) => (
  <Svg {...p}>
    <path d="M3 10.5L12 3l9 7.5M5 9.5V20a1 1 0 001 1h12a1 1 0 001-1V9.5" />
  </Svg>
);

export const Lista = (p: Props) => (
  <Svg {...p}>
    <path d="M8 6h13M8 12h13M8 18h13M3.5 6h.01M3.5 12h.01M3.5 18h.01" />
  </Svg>
);

export const Cedula = (p: Props) => (
  <Svg {...p}>
    <rect x="2" y="6" width="20" height="12" rx="2" />
    <circle cx="12" cy="12" r="2.5" />
    <path d="M6 12h.01M18 12h.01" />
  </Svg>
);

export const Escudo = (p: Props) => (
  <Svg {...p}>
    <path d="M12 3l7.5 3v6c0 4.5-3 7.7-7.5 9-4.5-1.3-7.5-4.5-7.5-9V6z" />
    <path d="M9.5 12l1.8 1.8 3.4-3.6" />
  </Svg>
);

export const Olho = (p: Props) => (
  <Svg {...p}>
    <path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z" />
    <circle cx="12" cy="12" r="3" />
  </Svg>
);

export const OlhoFechado = (p: Props) => (
  <Svg {...p}>
    <path d="M3 3l18 18M10.6 6.1A9.9 9.9 0 0112 6c6 0 9.5 6 9.5 6a17 17 0 01-3.3 4M6.5 8.2A17 17 0 002.5 12S6 18 12 18a9.6 9.6 0 003.5-.65" />
    <path d="M9.9 9.9a3 3 0 004.2 4.2" />
  </Svg>
);

export const Cadeado = (p: Props) => (
  <Svg {...p}>
    <rect x="4.5" y="10.5" width="15" height="10" rx="2" />
    <path d="M8 10.5V7.5a4 4 0 018 0v3" />
  </Svg>
);

export const Mais = (p: Props) => (
  <Svg {...p}>
    <path d="M12 5v14M5 12h14" />
  </Svg>
);

export const Seta = (p: Props) => (
  <Svg {...p}>
    <path d="M9 5l7 7-7 7" />
  </Svg>
);

export const Sair = (p: Props) => (
  <Svg {...p}>
    <path d="M15 17l5-5-5-5M20 12H9M12 3H6a1 1 0 00-1 1v16a1 1 0 001 1h6" />
  </Svg>
);
