import { useCallback, useEffect, useState } from "react";
import { extrato, type LinhaDoExtrato } from "../api/banco";
import { dinheiro } from "../api/cliente";
import { Aviso, Botao, Cartao, Carregando, Disco, Valor, Vazio, entrada } from "../componentes/base";
import * as Icone from "../componentes/icones";

const TAMANHO = 20;

/** `2026-09-07` do input vira o instante que a API espera. */
const inicioDoDia = (dia: string) => (dia ? new Date(`${dia}T00:00:00`).toISOString() : undefined);
const fimDoDia = (dia: string) => (dia ? new Date(`${dia}T23:59:59.999`).toISOString() : undefined);

const diaDe = (iso: string) => new Date(iso).toLocaleDateString("pt-BR");

const horaDe = (iso: string) =>
  new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });

/**
 * Agrupa em dias, preservando a ordem que veio da API.
 *
 * O agrupamento é só de apresentação: a ordem continua sendo a do servidor, e nenhuma
 * reordenação acontece aqui. Ordenar no cliente brigaria com o marcador da paginação, que
 * assume exatamente a ordem que o índice devolve.
 */
function porDia(linhas: LinhaDoExtrato[]) {
  const dias: { dia: string; linhas: LinhaDoExtrato[] }[] = [];

  for (const linha of linhas) {
    const dia = diaDe(linha.criadoEm);
    const ultimo = dias.at(-1);

    if (ultimo?.dia === dia) ultimo.linhas.push(linha);
    else dias.push({ dia, linhas: [linha] });
  }

  return dias;
}

export default function Extrato({ contaId }: { contaId: string }) {
  const [de, setDe] = useState("");
  const [ate, setAte] = useState("");
  const [linhas, setLinhas] = useState<LinhaDoExtrato[]>([]);
  const [proxima, setProxima] = useState<string | null>(null);
  const [erro, setErro] = useState<unknown>(null);
  const [carregando, setCarregando] = useState(true);

  /**
   * Carrega uma página.
   *
   * O marcador é o único estado que a paginação carrega: não existe número de página nem
   * total de linhas, porque o extrato cresce por cima e ambos estariam errados na segunda
   * página. Quando `marcador` é nulo, é a primeira página e a lista recomeça.
   */
  const carregar = useCallback(
    async (marcador: string | null) => {
      setCarregando(true);
      setErro(null);

      try {
        const pagina = await extrato(contaId, {
          de: inicioDoDia(de),
          ate: fimDoDia(ate),
          tamanho: TAMANHO,
          pagina: marcador,
        });

        setLinhas((anteriores) => (marcador ? [...anteriores, ...pagina.linhas] : pagina.linhas));
        setProxima(pagina.proximaPagina);
      } catch (falha) {
        setErro(falha);
      } finally {
        setCarregando(false);
      }
    },
    [contaId, de, ate],
  );

  useEffect(() => {
    void carregar(null);
  }, [carregar]);

  const entradas = linhas.filter((l) => l.efeito > 0).reduce((t, l) => t + l.efeito, 0);
  const saidas = linhas.filter((l) => l.efeito < 0).reduce((t, l) => t + l.efeito, 0);

  return (
    <div className="space-y-5">
      <Cartao titulo="Extrato">
        <div className="grid gap-3 sm:grid-cols-[1fr_1fr_auto] sm:items-end">
          <label className="block">
            <span className="mb-1.5 block text-xs font-semibold text-tinta-fraca">De</span>
            <input type="date" className={entrada} value={de} onChange={(e) => setDe(e.target.value)} />
          </label>
          <label className="block">
            <span className="mb-1.5 block text-xs font-semibold text-tinta-fraca">Até</span>
            <input type="date" className={entrada} value={ate} onChange={(e) => setAte(e.target.value)} />
          </label>
          <Botao variante="secundario" onClick={() => { setDe(""); setAte(""); }} disabled={!de && !ate}>
            Limpar
          </Botao>
        </div>

        {linhas.length > 0 && (
          <div className="mt-5 grid grid-cols-2 gap-3">
            <div className="rounded-xl bg-entrada/8 px-4 py-3">
              <p className="text-xs font-medium text-entrada">Entradas</p>
              <p className="numero mt-0.5 font-bold text-entrada">{dinheiro(entradas)}</p>
            </div>
            <div className="rounded-xl bg-saida/8 px-4 py-3">
              <p className="text-xs font-medium text-saida">Saídas</p>
              <p className="numero mt-0.5 font-bold text-saida">{dinheiro(Math.abs(saidas))}</p>
            </div>
          </div>
        )}

        <p className="mt-3 text-xs text-tinta-fraca/80">
          Somas do que está carregado na tela, e não do período inteiro — a paginação por
          marcador não conta o total de propósito.
        </p>
      </Cartao>

      <Aviso erro={erro} aoFechar={() => setErro(null)} />

      {linhas.length === 0 && !carregando ? (
        <Cartao>
          <Vazio>Nenhum lançamento no período.</Vazio>
        </Cartao>
      ) : (
        porDia(linhas).map(({ dia, linhas: doDia }) => (
          <Cartao key={dia}>
            <p className="mb-3 text-xs font-semibold tracking-wide text-tinta-fraca uppercase">{dia}</p>
            <ul className="divide-y divide-borda/70">
              {doDia.map((linha) => (
                <li key={linha.id} className="flex items-center gap-3 py-3 first:pt-0 last:pb-0">
                  <Disco efeito={linha.efeito}>
                    {linha.transferenciaId ? (
                      <Icone.Troca className="h-4 w-4" />
                    ) : linha.efeito < 0 ? (
                      <Icone.Saida className="h-4 w-4" />
                    ) : (
                      <Icone.Entrada className="h-4 w-4" />
                    )}
                  </Disco>

                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium">{linha.descricao}</p>
                    <p className="truncate text-xs text-tinta-fraca">
                      {horaDe(linha.criadoEm)} · {linha.origem}
                    </p>
                  </div>

                  <div className="text-right">
                    <Valor efeito={linha.efeito} />
                    <p className="numero text-xs text-tinta-fraca">
                      saldo {dinheiro(linha.saldoDepois)}
                    </p>
                  </div>
                </li>
              ))}
            </ul>
          </Cartao>
        ))
      )}

      {carregando && <Carregando />}

      {proxima && !carregando && (
        <div className="flex justify-center">
          <Botao variante="secundario" onClick={() => void carregar(proxima)}>
            Carregar mais
          </Botao>
        </div>
      )}

      {!proxima && linhas.length > 0 && !carregando && (
        <p className="text-center text-xs text-tinta-fraca/70">Fim do extrato.</p>
      )}
    </div>
  );
}
