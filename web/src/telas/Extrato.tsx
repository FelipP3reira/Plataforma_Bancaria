import { useCallback, useEffect, useState } from "react";
import { extrato, type LinhaDoExtrato } from "../api/banco";
import { dataHora, dinheiro } from "../api/cliente";
import { Aviso, Botao, Campo, Cartao, Carregando, Valor, Vazio, entrada } from "../componentes/base";

const TAMANHO = 20;

/** `2026-09-07` do input vira o instante que a API espera. */
const inicioDoDia = (dia: string) => (dia ? new Date(`${dia}T00:00:00`).toISOString() : undefined);
const fimDoDia = (dia: string) => (dia ? new Date(`${dia}T23:59:59.999`).toISOString() : undefined);

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

  return (
    <div className="space-y-6">
      <Cartao titulo="Período">
        <div className="grid gap-4 sm:grid-cols-[1fr_1fr_auto] sm:items-end">
          <Campo rotulo="De">
            <input type="date" className={entrada} value={de} onChange={(e) => setDe(e.target.value)} />
          </Campo>
          <Campo rotulo="Até">
            <input type="date" className={entrada} value={ate} onChange={(e) => setAte(e.target.value)} />
          </Campo>
          <Botao
            variante="secundario"
            onClick={() => {
              setDe("");
              setAte("");
            }}
            disabled={!de && !ate}
          >
            Limpar
          </Botao>
        </div>
      </Cartao>

      <Aviso erro={erro} aoFechar={() => setErro(null)} />

      <Cartao titulo="Extrato">
        {linhas.length === 0 && !carregando ? (
          <Vazio>Nenhum lançamento no período.</Vazio>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-borda text-left text-xs text-tinta/50">
                  <th className="py-2 pr-3 font-medium">Quando</th>
                  <th className="py-2 pr-3 font-medium">Descrição</th>
                  <th className="py-2 pr-3 font-medium">Origem</th>
                  <th className="py-2 pr-3 text-right font-medium">Valor</th>
                  <th className="py-2 text-right font-medium">Saldo</th>
                </tr>
              </thead>
              <tbody>
                {linhas.map((linha) => (
                  <tr key={linha.id} className="border-b border-borda/60 last:border-0">
                    <td className="numero py-3 pr-3 whitespace-nowrap text-tinta/60">
                      {dataHora(linha.criadoEm)}
                    </td>
                    <td className="py-3 pr-3">
                      {linha.descricao}
                      {linha.transferenciaId && (
                        <span className="ml-2 rounded bg-tinta/5 px-1.5 py-0.5 text-xs text-tinta/50">
                          transferência
                        </span>
                      )}
                    </td>
                    <td className="py-3 pr-3 text-xs text-tinta/45">{linha.origem}</td>
                    <td className="py-3 pr-3 text-right">
                      <Valor efeito={linha.efeito} />
                    </td>
                    <td className="numero py-3 text-right text-tinta/60">
                      {dinheiro(linha.saldoDepois)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {carregando && <Carregando />}

        {proxima && !carregando && (
          <div className="mt-4 flex justify-center">
            <Botao variante="secundario" onClick={() => void carregar(proxima)}>
              Carregar mais
            </Botao>
          </div>
        )}

        {!proxima && linhas.length > 0 && !carregando && (
          <p className="mt-4 text-center text-xs text-tinta/40">Fim do extrato.</p>
        )}
      </Cartao>
    </div>
  );
}
