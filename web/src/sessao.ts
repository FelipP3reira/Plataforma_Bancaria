/**
 * Quem está usando o app.
 *
 * ISTO NÃO É AUTENTICAÇÃO, e o app diz isso na tela. Não existe senha, não existe token, e
 * qualquer pessoa que saiba o id de uma conta entra nela. É um substituto explícito
 * enquanto autenticação de verdade não existe nas APIs — inventar uma tela de senha que
 * não valida nada seria pior, porque passaria a impressão de que valida.
 *
 * O que ele guarda é só a conta escolhida, no navegador. Nenhuma decisão de permissão
 * acontece aqui: as APIs continuam aceitando qualquer chamada, e é lá que a autorização
 * teria que morar.
 */

const CHAVE = "plataforma-bancaria:conta";

export type Sessao = { contaId: string; titular: string; numero: string };

export function ler(): Sessao | null {
  try {
    const guardado = localStorage.getItem(CHAVE);
    return guardado ? (JSON.parse(guardado) as Sessao) : null;
  } catch {
    // Navegador com armazenamento bloqueado ou conteúdo corrompido: cair na tela de
    // entrada é o comportamento certo, e melhor do que a tela toda quebrar.
    return null;
  }
}

export function gravar(sessao: Sessao) {
  try {
    localStorage.setItem(CHAVE, JSON.stringify(sessao));
  } catch {
    // Falhar em lembrar a conta é irritante; falhar em entrar seria pior. A sessão vive na
    // memória do React de qualquer jeito, então a navegação continua funcionando.
  }
}

export function limpar() {
  try {
    localStorage.removeItem(CHAVE);
  } catch {
    // Idem: sair da tela é o que importa, e quem chama já derrubou a sessão em memória.
  }
}

/**
 * O que vai no cabeçalho `X-Operador` de toda movimentação.
 *
 * Prefixo `web:` de propósito: no extrato dá para separar o que veio da interface do que
 * veio do Core de Crédito (`credito:desembolso`) ou de um operador interno.
 */
export const operadorDe = (sessao: Sessao) => `web:${sessao.titular}`;
