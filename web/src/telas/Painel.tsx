import { useState } from "react";
import { depositar, sacar, transferir, type Conta } from "../api/banco";
import { chaveNova, dinheiro } from "../api/cliente";
import { Aviso, Botao, Campo, Cartao, Selo, entrada } from "../componentes/base";
import { operadorDe, type Sessao } from "../sessao";

type Operacao = "deposito" | "saque" | "transferencia";

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
  const bloqueada = conta.estado !== "Ativa";

  return (
    <div className="space-y-6">
      <Cartao>
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <p className="text-xs text-tinta/50">Saldo disponível</p>
            <p className="numero mt-1 text-4xl font-semibold">{dinheiro(conta.saldo)}</p>
            <p className="mt-2 text-sm text-tinta/60">
              {conta.titular} · conta {conta.numero}
            </p>
          </div>
          <div className="text-right">
            <Selo estado={conta.estado} />
            <p className="mt-2 text-xs text-tinta/50">
              {conta.lancamentos} {conta.lancamentos === 1 ? "lançamento" : "lançamentos"}
            </p>
          </div>
        </div>

        <div className="mt-6 flex flex-wrap gap-2">
          {(Object.keys(titulos) as Operacao[]).map((operacao) => (
            <Botao
              key={operacao}
              variante={aberta === operacao ? "principal" : "secundario"}
              disabled={bloqueada}
              onClick={() => setAberta(aberta === operacao ? null : operacao)}
            >
              {titulos[operacao]}
            </Botao>
          ))}
        </div>

        {bloqueada && (
          <p className="mt-4 rounded-lg bg-amber-50 px-4 py-3 text-sm text-amber-900">
            Conta {conta.estado.toLowerCase()}: não aceita movimentação. O extrato e o
            histórico continuam disponíveis.
          </p>
        )}
      </Cartao>

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

  const numero = Number(valor.replace(",", "."));
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
          <Campo rotulo="Id da conta de destino">
            <input
              className={entrada}
              value={destino}
              onChange={(e) => setDestino(e.target.value)}
              placeholder="0199…"
            />
          </Campo>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <Campo rotulo="Valor" dica="Até duas casas decimais.">
            <input
              className={entrada}
              inputMode="decimal"
              value={valor}
              onChange={(e) => setValor(e.target.value)}
              placeholder="1000,00"
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
          <span className="text-xs text-tinta/45">
            Idempotency-Key: <code className="numero">{chave.slice(0, 8)}…</code> — a mesma em
            toda tentativa desta operação.
          </span>
        </div>
      </div>
    </Cartao>
  );
}
