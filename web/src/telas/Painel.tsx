import { useEffect, useState } from "react";
import {
  depositar,
  extrato,
  sacar,
  transferir,
  type Conta,
  type LinhaDoExtrato,
} from "../api/banco";
import { chaveNova, dataHora, dinheiro } from "../api/cliente";
import { Atalho, Aviso, Botao, Campo, Cartao, Disco, Selo, Valor, entrada } from "../componentes/base";
import * as Icone from "../componentes/icones";
import { operadorDe, type Sessao } from "../sessao";

type Operacao = "deposito" | "saque" | "transferencia";

const acoes = [
  { operacao: "deposito", rotulo: "Depositar", Desenho: Icone.Entrada },
  { operacao: "saque", rotulo: "Sacar", Desenho: Icone.Saida },
  { operacao: "transferencia", rotulo: "Transferir", Desenho: Icone.Troca },
] as const;

const titulos: Record<Operacao, string> = {
  deposito: "Depositar",
  saque: "Sacar",
  transferencia: "Transferir",
};

export default function Painel({
  conta,
  sessao,
  aoMudar,
}: {
  conta: Conta;
  sessao: Sessao;
  aoMudar: () => void;
}) {
  const [aberta, setAberta] = useState<Operacao | null>(null);
  const [escondido, setEscondido] = useState(false);
  const [ultimos, setUltimos] = useState<LinhaDoExtrato[]>([]);

  const bloqueada = conta.estado !== "Ativa";

  useEffect(() => {
    // Falha aqui não vira aviso: é uma prévia do extrato, e a aba do extrato mostra o erro
    // de verdade. Um alerta vermelho no topo do painel por causa da prévia assustaria à toa.
    extrato(conta.id, { tamanho: 5 })
      .then((pagina) => setUltimos(pagina.linhas))
      .catch(() => setUltimos([]));
  }, [conta.id, conta.lancamentos]);

  return (
    <div className="space-y-5">
      <div className="cartao-marca surgir relative overflow-hidden rounded-3xl p-6 text-white sm:p-7">
        <div className="pointer-events-none absolute -top-16 -right-12 h-52 w-52 rounded-full bg-white/10" />
        <div className="pointer-events-none absolute -bottom-24 -left-10 h-56 w-56 rounded-full bg-white/5" />

        <div className="relative flex items-start justify-between gap-4">
          <div>
            <div className="flex items-center gap-2">
              <p className="text-sm text-white/70">Saldo disponível</p>
              <button
                onClick={() => setEscondido((atual) => !atual)}
                className="text-white/60 transition hover:text-white"
                aria-label={escondido ? "Mostrar saldo" : "Esconder saldo"}
              >
                {escondido ? <Icone.OlhoFechado className="h-4 w-4" /> : <Icone.Olho className="h-4 w-4" />}
              </button>
            </div>

            <p className="numero mt-1.5 text-4xl font-bold sm:text-[2.75rem]">
              {escondido ? "•••••••" : dinheiro(conta.saldo)}
            </p>
          </div>

          <Selo estado={conta.estado} />
        </div>

        <div className="relative mt-7 flex items-end justify-between gap-4 text-sm">
          <div>
            <p className="text-xs text-white/60">Titular</p>
            <p className="font-semibold">{conta.titular}</p>
          </div>
          <div className="text-right">
            <p className="text-xs text-white/60">Conta</p>
            <p className="numero font-semibold">{conta.numero}</p>
          </div>
        </div>
      </div>

      <div className="flex justify-center gap-4 sm:justify-start sm:gap-6">
        {acoes.map(({ operacao, rotulo, Desenho }) => (
          <Atalho
            key={operacao}
            rotulo={rotulo}
            ativo={aberta === operacao}
            disabled={bloqueada}
            icone={<Desenho className="h-6 w-6" />}
            onClick={() => setAberta(aberta === operacao ? null : operacao)}
          />
        ))}
      </div>

      {bloqueada && (
        <p className="surgir flex items-start gap-3 rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
          <Icone.Cadeado className="mt-0.5 h-5 w-5 shrink-0" />
          <span>
            Conta {conta.estado.toLowerCase()}: não aceita movimentação. O extrato e o
            histórico continuam disponíveis.
          </span>
        </p>
      )}

      {aberta && (
        <Movimentar
          key={aberta}
          operacao={aberta}
          conta={conta}
          sessao={sessao}
          aoConcluir={() => {
            setAberta(null);
            aoMudar();
          }}
        />
      )}

      <Cartao titulo="Últimos lançamentos">
        {ultimos.length === 0 ? (
          <p className="py-6 text-center text-sm text-tinta-fraca">
            Nada por aqui ainda. O primeiro depósito aparece nesta lista.
          </p>
        ) : (
          <ul className="divide-y divide-borda/70">
            {ultimos.map((linha) => (
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
                  <p className="text-xs text-tinta-fraca">{dataHora(linha.criadoEm)}</p>
                </div>
                <Valor efeito={linha.efeito} />
              </li>
            ))}
          </ul>
        )}
      </Cartao>
    </div>
  );
}

/**
 * Um formulário de movimentação, com a chave de idempotência que ele carrega.
 *
 * A chave nasce junto do formulário e sobrevive a erro: se a primeira tentativa falhar por
 * rede, a segunda vai com a MESMA chave, e a API devolve o lançamento que já existe em vez
 * de criar um segundo. Gerar a chave dentro do clique faria dois cliques virarem dois
 * depósitos — que é exatamente o bug que o cabeçalho existe para impedir.
 *
 * Ela só é trocada quando a operação conclui, porque aí a próxima é outra operação.
 */
function Movimentar({
  operacao,
  conta,
  sessao,
  aoConcluir,
}: {
  operacao: Operacao;
  conta: Conta;
  sessao: Sessao;
  aoConcluir: () => void;
}) {
  const [chave] = useState(chaveNova);
  const [valor, setValor] = useState("");
  const [descricao, setDescricao] = useState("");
  const [destino, setDestino] = useState("");
  const [erro, setErro] = useState<unknown>(null);
  const [ocupado, setOcupado] = useState(false);

  const numero = Number(valor.replace(/\./g, "").replace(",", "."));
  const valido =
    Number.isFinite(numero) &&
    numero > 0 &&
    descricao.trim().length > 0 &&
    (operacao !== "transferencia" || destino.trim().length >= 10);

  async function enviar() {
    setOcupado(true);
    setErro(null);

    const movimento = {
      valor: numero,
      descricao: descricao.trim(),
      chave,
      operador: operadorDe(sessao),
    };

    try {
      if (operacao === "deposito") await depositar(conta.id, movimento);
      else if (operacao === "saque") await sacar(conta.id, movimento);
      else await transferir(conta.id, destino.trim(), movimento);

      aoConcluir();
    } catch (falha) {
      setErro(falha);
    } finally {
      setOcupado(false);
    }
  }

  return (
    <Cartao titulo={titulos[operacao]}>
      <div className="space-y-4">
        <Aviso erro={erro} aoFechar={() => setErro(null)} />

        {operacao === "transferencia" && (
          <Campo rotulo="Conta de destino" dica="O id da conta que vai receber.">
            <input
              className={entrada}
              value={destino}
              onChange={(e) => setDestino(e.target.value)}
              placeholder="01a07dc4-…"
            />
          </Campo>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <Campo rotulo="Valor" dica="Até duas casas decimais.">
            <input
              className={`${entrada} numero text-lg font-semibold`}
              inputMode="decimal"
              value={valor}
              onChange={(e) => setValor(e.target.value)}
              placeholder="0,00"
            />
          </Campo>
          <Campo rotulo="Descrição">
            <input
              className={entrada}
              value={descricao}
              onChange={(e) => setDescricao(e.target.value)}
              placeholder="salário"
              maxLength={140}
            />
          </Campo>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <Botao onClick={enviar} disabled={!valido || ocupado}>
            {ocupado ? "Enviando…" : "Confirmar"}
          </Botao>
          <span className="text-xs text-tinta-fraca/80">
            Idempotency-Key <code className="numero">{chave.slice(0, 8)}</code> — a mesma em
            toda tentativa desta operação.
          </span>
        </div>
      </div>
    </Cartao>
  );
}
