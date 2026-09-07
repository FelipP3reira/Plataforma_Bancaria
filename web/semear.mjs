/**
 * Dados de demonstração: contas com movimento, transferências entre elas e um empréstimo
 * em andamento.
 *
 * Roda contra as duas APIs pela borda pública, como qualquer cliente — não escreve no
 * banco de dados. É mais lento do que um INSERT em massa, e é de propósito: dado semeado
 * por fora do domínio nasceria sem passar pela máquina de estados nem pelo ledger, e o
 * app mostraria um saldo que a conciliação recusaria.
 *
 * Uma limitação honesta: todos os lançamentos ficam com a data de hoje. O instante vem do
 * relógio do servidor no momento do pedido, e não do corpo da requisição — deixar o
 * cliente escolher a data de um lançamento seria abrir a porta para forjar extrato.
 *
 *   node semear.mjs
 */

const BANCO = process.env.VITE_API_BANCO ?? "http://localhost:5240";
const CREDITO = process.env.VITE_API_CREDITO ?? "http://localhost:5250";

const chave = () => crypto.randomUUID();

async function chamar(base, rota, { metodo = "GET", corpo, cabecalhos = {} } = {}) {
  const resposta = await fetch(`${base}${rota}`, {
    method: metodo,
    headers: { ...(corpo ? { "Content-Type": "application/json" } : {}), ...cabecalhos },
    body: corpo ? JSON.stringify(corpo) : undefined,
  });

  const texto = await resposta.text();

  if (!resposta.ok) {
    const problema = texto ? JSON.parse(texto) : {};
    throw new Error(`${metodo} ${rota} → ${resposta.status} ${problema.title ?? ""} ${problema.detail ?? ""}`);
  }

  return texto ? JSON.parse(texto) : null;
}

const banco = (rota, opcoes) => chamar(BANCO, rota, opcoes);
const credito = (rota, opcoes) => chamar(CREDITO, rota, opcoes);

const operador = (titular) => ({ "X-Operador": `semeadura:${titular}` });

const abrirConta = (titular) => banco("/contas", { metodo: "POST", corpo: { titular } });

const movimentar = (conta, rota, valor, descricao) =>
  banco(`/contas/${conta.id}/${rota}`, {
    metodo: "POST",
    corpo: { valor, descricao },
    cabecalhos: { "Idempotency-Key": chave(), ...operador(conta.titular) },
  });

const depositar = (conta, valor, descricao) => movimentar(conta, "depositos", valor, descricao);
const sacar = (conta, valor, descricao) => movimentar(conta, "saques", valor, descricao);

const transferir = (origem, destino, valor, descricao) =>
  banco(`/contas/${origem.id}/transferencias`, {
    metodo: "POST",
    corpo: { contaDestinoId: destino.id, valor, descricao },
    cabecalhos: { "Idempotency-Key": chave(), ...operador(origem.titular) },
  });

/** CPF com dígitos verificadores válidos — o domínio recusa qualquer outro. */
function cpfValido() {
  const base = Array.from({ length: 9 }, () => Math.floor(Math.random() * 10));

  for (let i = 0; i < 2; i++) {
    const peso = base.length + 1;
    const soma = base.reduce((total, digito, indice) => total + digito * (peso - indice), 0);
    const resto = (soma * 10) % 11;
    base.push(resto === 10 ? 0 : resto);
  }

  return base.join("");
}

/**
 * O birô simulado deriva o score do CPF, então nem todo cliente é aprovado.
 * Recusa é resultado legítimo; aqui a semeadura insiste até achar um aprovável para
 * poder deixar um empréstimo em andamento na tela.
 */
async function emprestimoAprovado(conta, valorSolicitado, prazoEmMeses) {
  for (let tentativa = 0; tentativa < 60; tentativa++) {
    const proposta = await credito("/propostas", {
      metodo: "POST",
      corpo: {
        cpf: cpfValido(),
        nomeSolicitante: conta.titular,
        dataDeNascimento: "1992-06-21",
        rendaMensal: 9000,
        valorSolicitado,
        prazoEmMeses,
        sistema: "Price",
      },
      cabecalhos: { "Idempotency-Key": chave() },
    });

    const decisao = await credito(`/propostas/${proposta.id}/analise`, { metodo: "POST" });
    if (decisao.aprovada) return proposta.id;
  }

  throw new Error("Não achei um CPF que o birô aprove. Rode de novo.");
}

const compras = [
  ["mercado", 312.4], ["farmacia", 87.9], ["combustivel", 250], ["restaurante", 96.5],
  ["assinatura de streaming", 39.9], ["academia", 129], ["livraria", 74.3], ["padaria", 28.6],
  ["conta de luz", 187.2], ["internet", 119.9], ["transporte", 43.75], ["presente", 210],
  ["cinema", 64], ["cafe", 18.5], ["material de escritorio", 155.4], ["seguro", 98],
  ["dentista", 320], ["pet shop", 145.6], ["feira", 87.25], ["telefone", 69.9],
];

console.log(`Banco:   ${BANCO}`);
console.log(`Crédito: ${CREDITO}\n`);

const ana = await abrirConta("Ana Ribeiro");
const bruno = await abrirConta("Bruno Salgado");
const clara = await abrirConta("Clara Nunes");
const diego = await abrirConta("Diego Prado");

console.log("Contas abertas.");

await depositar(ana, 8500, "salario");
await depositar(bruno, 4200, "salario");
await depositar(clara, 6100, "salario");
await depositar(diego, 3300, "salario");

// Volume suficiente para o extrato paginar de verdade e o filtro de período ter o que
// filtrar: a Ana passa de vinte lançamentos, mais de uma página no tamanho padrão.
for (const [descricao, valor] of compras) {
  await sacar(ana, valor, descricao);
}

for (const [descricao, valor] of compras.slice(0, 6)) {
  await sacar(bruno, valor, descricao);
}

await transferir(ana, bruno, 450, "aluguel");
await transferir(bruno, clara, 120, "racha do jantar");
await transferir(clara, ana, 300, "devolucao");
await transferir(ana, diego, 80, "vaquinha");

console.log("Movimentação lançada.");

// Um empréstimo em andamento: contratado, desembolsado e com duas parcelas pagas.
const proposta = await emprestimoAprovado(clara, 12000, 24);
await credito(`/propostas/${proposta}/contrato`, { metodo: "POST", corpo: { contaId: clara.id } });
await credito(`/propostas/${proposta}/contrato/desembolso`, { metodo: "POST" });

for (const numero of [1, 2]) {
  await credito(`/propostas/${proposta}/contrato/parcelas/${numero}/pagamento`, {
    metodo: "POST",
    cabecalhos: { "Idempotency-Key": chave() },
  });
}

console.log("Empréstimo contratado, desembolsado e com duas parcelas pagas.");

// Uma conta bloqueada, para a trilha de estados ter o que mostrar.
await banco(`/contas/${diego.id}/bloqueio`, {
  metodo: "POST",
  corpo: { motivo: "movimentacao atipica em apuracao" },
  cabecalhos: operador("compliance"),
});

console.log("Conta do Diego bloqueada.\n");

for (const conta of [ana, bruno, clara, diego]) {
  const atual = await banco(`/contas/${conta.id}`);
  const conferencia = await banco(`/contas/${conta.id}/conciliacao`);

  console.log(
    `${atual.titular.padEnd(16)} ${atual.numero}  ` +
      `${atual.saldo.toFixed(2).padStart(10)}  ${atual.estado.padEnd(10)} ` +
      `${atual.lancamentos} lanç.  concilia: ${conferencia.bate}`,
  );
  console.log(`   id: ${conta.id}`);
}

console.log("\nEntre no app com um desses ids: http://localhost:5173");
