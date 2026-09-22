import { useEffect, useRef, useState } from "react";
import { dinheiro } from "../api/cliente";
import {
  ESTADOS,
  ROTULOS,
  documento as buscarDocumento,
  enviarDocumento,
  pagarDocumento,
  reprocessarDocumento,
  revisar,
  type CampoDoDocumento,
  type Documento,
  type NomeDoCampo,
} from "../api/documentos";
import { Aviso, Botao, Campo, Cartao, Vazio, entrada } from "../componentes/base";
import * as Icone from "../componentes/icones";
import { operadorDe, type Sessao } from "../sessao";

/**
 * Enquanto o documento está na fila ou sendo lido, a tela pergunta de novo.
 *
 * Três segundos, e não menos: o worker acorda nesse ritmo, e perguntar mais rápido só
 * gastaria requisição para receber o mesmo estado. É consulta periódica e não algo em tempo
 * real de propósito — um canal aberto por documento enviado custaria mais do que a espera de
 * alguns segundos que ele economiza.
 */
const ESPERA_ENTRE_CONSULTAS = 3000;

export default function Boletos({
  sessao,
  aoMudarSaldo,
}: {
  sessao: Sessao;
  aoMudarSaldo: () => void;
}) {
  const [documento, setDocumento] = useState<Documento | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [reenvio, setReenvio] = useState(false);
  const [erro, setErro] = useState<unknown>(null);
  const seletor = useRef<HTMLInputElement>(null);

  const operador = operadorDe(sessao);
  const esperando = documento !== null && ESTADOS[documento.estado].espera;

  /**
   * Repergunta enquanto o documento não terminou de ser lido.
   *
   * O intervalo é derrubado quando o efeito é refeito, e é por isso que ele depende do estado
   * e não só do id: sem essa dependência o `setInterval` continuaria rodando depois de o
   * documento ficar pronto.
   */
  useEffect(() => {
    if (!documento || !esperando) return;

    const id = documento.id;
    const relogio = setInterval(async () => {
      try {
        setDocumento(await buscarDocumento(id));
      } catch (falha) {
        setErro(falha);
      }
    }, ESPERA_ENTRE_CONSULTAS);

    return () => clearInterval(relogio);
  }, [documento, esperando]);

  async function enviar(arquivo: File) {
    setEnviando(true);
    setErro(null);
    setReenvio(false);

    try {
      const enviado = await enviarDocumento(sessao.contaId, arquivo, operador);

      setReenvio(!enviado.novo);
      setDocumento(await buscarDocumento(enviado.id));
    } catch (falha) {
      setErro(falha);
    } finally {
      setEnviando(false);

      // Limpa o seletor para que escolher o MESMO arquivo de novo dispare o evento. Sem isto,
      // o navegador não avisa nada na segunda escolha, e a pessoa conclui que o app travou.
      if (seletor.current) seletor.current.value = "";
    }
  }

  async function agir(acao: () => Promise<unknown>) {
    setErro(null);

    try {
      await acao();
      setDocumento(await buscarDocumento(documento!.id));
      aoMudarSaldo();
    } catch (falha) {
      setErro(falha);
    }
  }

  return (
    <>
      <Aviso erro={erro} aoFechar={() => setErro(null)} />

      <Cartao titulo="Pagar boleto">
        <p className="mb-4 text-sm text-tinta-fraca">
          Manda o PDF ou a foto do boleto. O sistema lê a linha digitável, confere os dígitos
          verificadores e diz quanto vai ser debitado antes de debitar.
        </p>

        <label
          className={`flex cursor-pointer flex-col items-center gap-2 rounded-2xl border border-dashed border-borda bg-papel px-5 py-8 text-center transition hover:border-marca/50 ${
            enviando ? "pointer-events-none opacity-60" : ""
          }`}
        >
          <span className="flex h-12 w-12 items-center justify-center rounded-full bg-white text-marca">
            <Icone.Entrada />
          </span>
          <span className="text-sm font-semibold">
            {enviando ? "Enviando…" : "Escolher arquivo"}
          </span>
          <span className="text-xs text-tinta-fraca">PDF, PNG ou JPEG, até 10 MB</span>
          <input
            ref={seletor}
            type="file"
            // A lista é uma comodidade do seletor de arquivos, e não a validação: o
            // servidor decide pelos primeiros bytes, porque `accept` é só uma sugestão que
            // qualquer pessoa contorna.
            accept=".pdf,.png,.jpg,.jpeg"
            className="hidden"
            onChange={(evento) => {
              const arquivo = evento.target.files?.[0];
              if (arquivo) void enviar(arquivo);
            }}
          />
        </label>
      </Cartao>

      {reenvio && (
        <p className="rounded-xl bg-papel px-4 py-3 text-sm text-tinta-fraca">
          Este arquivo já tinha sido enviado. É o mesmo documento — o sistema reconhece pelo
          conteúdo, então não há risco de pagar duas vezes.
        </p>
      )}

      {documento && (
        <Resultado
          documento={documento}
          operador={operador}
          aoConferir={(correcoes) => agir(() => revisar(documento.id, correcoes, operador))}
          aoPagar={() => agir(() => pagarDocumento(documento.id, operador))}
          aoReprocessar={() => agir(() => reprocessarDocumento(documento.id, operador))}
        />
      )}
    </>
  );
}

function Resultado({
  documento,
  operador,
  aoConferir,
  aoPagar,
  aoReprocessar,
}: {
  documento: Documento;
  operador: string;
  aoConferir: (correcoes: Partial<Record<NomeDoCampo, string>>) => Promise<void>;
  aoPagar: () => Promise<void>;
  aoReprocessar: () => Promise<void>;
}) {
  const estado = ESTADOS[documento.estado];

  if (estado.espera) {
    return (
      <Cartao titulo={documento.nomeOriginal}>
        <Vazio>
          {documento.estado === "Recebido"
            ? "Na fila. O worker de extração pega o documento em alguns segundos."
            : "Lendo o documento…"}
        </Vazio>
      </Cartao>
    );
  }

  if (documento.estado === "Falhou") {
    return (
      <Cartao titulo={documento.nomeOriginal} acao={<Etiqueta texto={estado.rotulo} tom="perigo" />}>
        <p className="text-sm text-tinta-fraca">
          A leitura falhou {documento.tentativas} vezes e o sistema parou de tentar.
          {documento.ultimoErro && (
            <>
              {" "}
              Último erro: <span className="numero">{documento.ultimoErro}</span>.
            </>
          )}
        </p>
        <Botao variante="secundario" className="mt-4" onClick={() => void aoReprocessar()}>
          Tentar de novo
        </Botao>
      </Cartao>
    );
  }

  return (
    <Campos
      documento={documento}
      operador={operador}
      aoConferir={aoConferir}
      aoPagar={aoPagar}
    />
  );
}

function Campos({
  documento,
  operador,
  aoConferir,
  aoPagar,
}: {
  documento: Documento;
  operador: string;
  aoConferir: (correcoes: Partial<Record<NomeDoCampo, string>>) => Promise<void>;
  aoPagar: () => Promise<void>;
}) {
  const [correcoes, setCorrecoes] = useState<Partial<Record<NomeDoCampo, string>>>({});
  const [conferindo, setConferindo] = useState(documento.estado === "RequerRevisao");

  const estado = ESTADOS[documento.estado];
  const precisaConferir = documento.estado === "RequerRevisao";
  const pago = documento.estado === "Pago";
  const valor = documento.campos.find((campo) => campo.nome === "Valor")?.valorFinal;

  return (
    <Cartao
      titulo={documento.nomeOriginal}
      acao={
        <Etiqueta
          texto={estado.rotulo}
          tom={pago ? "ok" : precisaConferir ? "atencao" : "neutro"}
        />
      }
    >
      {precisaConferir && (
        <p className="mb-5 rounded-xl bg-amber-50 px-4 py-3 text-sm text-amber-900">
          A leitura não fechou com confiança suficiente ({porcento(documento.confianca)}).
          Confira os campos abaixo antes de pagar.
        </p>
      )}

      <dl className="space-y-4">
        {documento.campos.map((campo) => (
          <Linha
            key={campo.nome}
            campo={campo}
            editavel={conferindo}
            valor={correcoes[campo.nome] ?? campo.valorFinal}
            aoDigitar={(novo) => setCorrecoes({ ...correcoes, [campo.nome]: novo })}
          />
        ))}
      </dl>

      {documento.campos.length === 0 && (
        <Vazio>
          Nada foi reconhecido neste arquivo. Ele foi lido, mas não é um boleto de cobrança
          bancária — ou a imagem não deixou a linha digitável legível.
        </Vazio>
      )}

      <div className="mt-6 flex flex-wrap items-center gap-3">
        {conferindo ? (
          <>
            <Botao
              onClick={async () => {
                await aoConferir(correcoes);
                setConferindo(false);
                setCorrecoes({});
              }}
            >
              Confirmar conferência
            </Botao>
            {!precisaConferir && (
              <Botao variante="secundario" onClick={() => setConferindo(false)}>
                Cancelar
              </Botao>
            )}
          </>
        ) : pago ? (
          <p className="text-sm text-entrada">
            Debitado da conta{valor ? ` — ${dinheiro(Number(valor))}` : ""}. O lançamento está no
            extrato.
          </p>
        ) : (
          <>
            <Botao onClick={() => void aoPagar()}>
              Pagar {valor ? dinheiro(Number(valor)) : "boleto"}
            </Botao>
            <Botao variante="secundario" onClick={() => setConferindo(true)}>
              Corrigir algo
            </Botao>
          </>
        )}
      </div>

      <p className="mt-5 text-xs text-tinta-fraca/75">
        Enviado por {documento.origem}
        {documento.campos.some((campo) => campo.corrigidoPor) && ` · conferido por ${operador}`}
      </p>
    </Cartao>
  );
}

/**
 * Uma linha de campo, com o quanto se pode confiar nela.
 *
 * Mostra o valor que a máquina leu logo abaixo quando alguém corrigiu, porque os dois
 * existem no servidor e a diferença entre eles é a informação mais útil da tela: é o que
 * mostra onde a extração erra.
 */
function Linha({
  campo,
  editavel,
  valor,
  aoDigitar,
}: {
  campo: CampoDoDocumento;
  editavel: boolean;
  valor: string;
  aoDigitar: (valor: string) => void;
}) {
  const fraco = campo.confianca < 0.8;

  return (
    <div>
      {editavel ? (
        <Campo rotulo={ROTULOS[campo.nome]} dica={campo.observacao ?? undefined}>
          <input
            className={`${entrada} numero`}
            value={valor}
            onChange={(evento) => aoDigitar(evento.target.value)}
          />
        </Campo>
      ) : (
        <>
          <dt className="mb-1 flex items-center gap-2 text-xs font-semibold text-tinta-fraca">
            {ROTULOS[campo.nome]}
            <span
              className={`rounded-full px-2 py-0.5 text-[11px] font-semibold ${
                fraco ? "bg-amber-100 text-amber-800" : "bg-entrada/10 text-entrada"
              }`}
              // O título explica de onde vem a confiança sem ocupar a linha: campo conferido
              // por dígito verificador não depende de o modelo achar que leu bem.
              title={
                campo.origem === "Estrutura"
                  ? "Conferido por dígito verificador"
                  : "Encontrado por formato no texto, sem nada confirmando"
              }
            >
              {porcento(campo.confianca)}
            </span>
          </dt>
          <dd className="numero break-all text-sm font-medium">{campo.valorFinal || "—"}</dd>

          {campo.corrigidoPor && (
            <dd className="mt-1 text-xs text-tinta-fraca/75">
              A máquina tinha lido <span className="numero">{campo.valorLido || "nada"}</span> ·
              corrigido por {campo.corrigidoPor}
            </dd>
          )}

          {!campo.corrigidoPor && campo.observacao && (
            <dd className="mt-1 text-xs text-tinta-fraca/75">{campo.observacao}</dd>
          )}
        </>
      )}
    </div>
  );
}

function Etiqueta({ texto, tom }: { texto: string; tom: "ok" | "atencao" | "perigo" | "neutro" }) {
  const cores = {
    ok: "bg-entrada/10 text-entrada",
    atencao: "bg-amber-100 text-amber-800",
    perigo: "bg-saida/10 text-saida",
    neutro: "bg-tinta/10 text-tinta-fraca",
  } as const;

  return (
    <span className={`rounded-full px-3 py-1 text-xs font-semibold ${cores[tom]}`}>{texto}</span>
  );
}

const porcento = (confianca: number | null) =>
  confianca === null ? "—" : `${Math.round(confianca * 100)}%`;
