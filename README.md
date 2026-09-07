# Plataforma Bancária

Uma fatia de core banking: contas, ledger, saldo e extrato. O ponto não é o CRUD de conta —
é o que um sistema que guarda dinheiro precisa ter para não perder nem inventar nenhum:
lançamento imutável, saldo que se prova contra o ledger, e duas requisições simultâneas
sacando da mesma conta terminando do único jeito certo.

## Estado atual

Fatia 1: conta, ledger, depósito, saque, saldo materializado e reconciliação.

```
POST /contas                        abre conta (nasce zerada)
GET  /contas/{id}                   saldo, titular e quantidade de lançamentos
POST /contas/{id}/depositos         credita (exige Idempotency-Key e X-Operador)
POST /contas/{id}/saques            debita, recusando se não houver saldo
GET  /contas/{id}/conciliacao       confere o saldo materializado contra o ledger
```

O que já está escrito está testado; o que falta está listado no fim.

## Como rodar

Precisa de .NET 10 e Docker.

```bash
cp .env.example .env
```

A senha do SA aparece duas vezes no `.env`: em `SENHA_SA`, que o compose usa ao criar o
container, e dentro de `ConnectionStrings__Banco`. Têm que ser a mesma.

```bash
docker compose up -d
dotnet tool restore
dotnet ef database update --project src/Banco.Infraestrutura --startup-project src/Banco.Api
dotnet run --project src/Banco.Api
```

O SQL Server sobe na porta **1434**, e não na 1433. O Core de Crédito roda o próprio banco
na porta padrão, e os dois precisam subir juntos quando a integração entre eles entrar.

### Testes

```bash
dotnet test
```

Os de unidade não precisam de nada. Os de integração sobem um SQL Server próprio via
Testcontainers, então precisam do Docker no ar — e não encostam no banco de desenvolvimento.

## Camadas

```
Banco.Dominio          zero dependências. Conta, ledger, dinheiro, número de conta.
Banco.Aplicacao        casos de uso e portas (repositório, unidade de trabalho, conciliação).
Banco.Infraestrutura   EF Core, migrações, a trava pessimista, a consulta de conferência.
Banco.Api              rotas, validação de borda, tradução de erro.
```

A dependência anda em um sentido só: `Api → Aplicacao → Dominio`, e a `Infraestrutura` entra
pela `Aplicacao`. A `Api` só encosta na `Infraestrutura` no registro da injeção de
dependência.

## O ledger

Toda movimentação é uma linha nova. Não existe `UPDATE` no saldo por fora de um lançamento,
e não existe caminho para alterar ou apagar um lançamento — estorno é uma linha em sentido
contrário, e não a edição do original.

Cada linha guarda:

| campo | por quê |
|---|---|
| `Sequencia` | posição na conta, de 1 em diante, sem buraco |
| `Valor` | sempre positivo; o sentido está no `Tipo` |
| `SaldoDepois` | o saldo logo após esta linha |
| `ChaveIdempotencia` | a chave que o cliente mandou, guardada e não só conferida |
| `Origem` | quem mandou fazer |
| `CriadoEm` | quando |

### O saldo é um cache, e a conferência é explícita

O saldo mora na linha da conta, gravado na mesma transação do lançamento. Não é a verdade —
o ledger é. É um cache, e existe porque a alternativa honesta, somar o ledger a cada
movimentação, faria o custo de um saque depender de quantos saques já houve: uma conta com
dez mil lançamentos leria dez mil linhas, sob trava, para gravar a décima mil e uma.

Por isso o agregado **não carrega o ledger**. `Conta` guarda `Saldo` e `UltimaSequencia`, os
dois campos de que precisa para produzir o próximo lançamento em tempo constante.

Cache mente quando ninguém confere, então a conferência é uma rota:
`GET /contas/{id}/conciliacao`. Ela roda no banco, numa consulta só, e olha quatro coisas ao
mesmo tempo:

1. o saldo materializado bate com a soma do ledger;
2. a `UltimaSequencia` da conta bate com a maior sequência do ledger;
3. a contagem de lançamentos bate com essa mesma sequência — sem buraco;
4. a corrente fecha: cada `SaldoDepois` é o anterior mais o próprio efeito.

Os quatro juntos, e não um. Saldo batendo com a soma não basta — dois lançamentos
adulterados que se anulem dariam a mesma soma. É por isso que o `SaldoDepois` fica gravado:
ele transforma a conferência numa verificação de corrente com `LAG`, em vez de uma soma que
não enxerga esse caso. Tem teste que adultera exatamente assim, por fora da API, e exige que
a conferência acuse.

### `SaldoDepois` também paga o extrato

Guardado, ele deixa o extrato mostrar saldo corrente linha a linha sem recalcular nada — e
`Conta.Lancamentos` sai da `UltimaSequencia`, sem `COUNT`.

## Concorrência: trava pessimista, e por quê

A leitura da conta num caminho que vai movimentar passa por `PorIdParaMovimentar`, que lê
com `WITH (UPDLOCK, ROWLOCK)` dentro de uma transação em `READ COMMITTED`. A linha fica
segura até o commit.

**Por que não lock otimista.** Debitar é ler-decidir-gravar sobre a *mesma linha*, e a
contenção é alta justamente onde importa. Com `rowversion`, duas transferências simultâneas
na mesma conta colidem, entram em repetição, e sob contenção real isso vira latência
explodindo — cada tentativa refaz o trabalho para descobrir de novo que perdeu. Com trava, o
banco enfileira: uma passa, a outra lê o saldo já debitado e recebe **saldo insuficiente**,
que é a resposta de negócio correta e não um erro técnico a ser reprocessado.

**Por que não `SERIALIZABLE`.** O que precisa de proteção aqui é uma linha específica sendo
lida e regravada, e a trava explícita já resolve isso. `SERIALIZABLE` cobriria o mesmo caso
pegando travas de faixa em tudo que a transação encostasse, encarecendo qualquer consulta de
extrato que rodasse junto para proteger contra leitura fantasma — que aqui não existe, porque
ninguém decide nada a partir de um conjunto de linhas.

**O preço.** A trava só vale dentro de uma transação, então quem lê para movimentar tem que
abrir uma. E quando a transferência entrar (fatia 2), duas contas serão travadas na mesma
operação — o que exige ordem de aquisição fixa, senão `A→B` junto com `B→A` deadlocka.

### A rede embaixo da trava

Há um índice único em `(ContaId, Sequencia)`. Ele não é redundante com a trava: é o que pega
a trava tendo falhado. Se um caminho novo esquecer o `UPDLOCK`, ou abrir a transação no
lugar errado, duas gravações concorrentes que leram o mesmo saldo tentam gravar a mesma
posição, e o banco recusa a segunda. Sem ele, a falha da trava viraria dinheiro duplicado em
silêncio.

Isso apareceu num teste. A primeira versão do teste de dois saques simultâneos verificava só
os códigos de status — e **passava com o `UPDLOCK` removido**, porque os dois saques
colidiam no índice único e um deles voltava 409 mesmo assim. Estava provando o índice, não a
trava. O teste hoje verifica o *motivo* da recusa: tem que ser "saldo insuficiente", que só
acontece se a segunda requisição decidiu depois de a primeira terminar.

Com a trava, 4 dos 5 testes de concorrência passam; sem ela, os mesmos 4 falham. O quinto —
saques em contas diferentes não disputam entre si — passa nos dois casos, e é isso mesmo que
tem que acontecer.

## Idempotência

Toda operação financeira exige `Idempotency-Key`. A chave fica gravada na linha do ledger, e
é por ela que um reenvio encontra o lançamento que já existe em vez de criar um segundo.

A conferência de reenvio acontece **depois** de pegar a trava, e não antes. Antes dela, cinco
requisições com a mesma chave passariam juntas pela consulta, as cinco se achariam a
primeira, e o índice único recusaria quatro — com uma delas já tendo movido o saldo, e as
outras quatro devolvendo erro em vez do resultado correto. Tem teste disparando cinco
reenvios ao mesmo tempo: todos respondem sucesso, e a conta é creditada uma vez só.

A chave vale **por conta**, não globalmente: dois clientes diferentes podem legitimamente
mandar a mesma chave para contas diferentes.

Mesma chave com valor ou tipo diferente não é reenvio — é chave reaproveitada por engano, e
devolver o lançamento antigo confirmaria uma operação que ninguém pediu. Isso é 409.

## Decisões e trade-offs

### Dinheiro é um tipo, não um `decimal`

`decimal` e nunca `double`: ponto flutuante binário não representa 0,10 exatamente, e num
ledger o erro não se dilui — ele se acumula lançamento a lançamento até o saldo divergir da
soma.

Mas `decimal` solto ainda aceita valor negativo e três casas. O tipo `Dinheiro` recusa os
dois, e a própria subtração recusa resultado negativo. O efeito é que **nenhum caminho de
código consegue produzir um saldo negativo**, nem um caso de uso futuro que esqueça de
conferir antes — não há cheque especial neste sistema, e a garantia não depende de ninguém
lembrar.

Valor com mais de duas casas é recusado em vez de arredondado. Quem manda 10,999 acha que
mandou dez reais e noventa e nove; arredondar em silêncio faria o extrato mostrar outra coisa.

### Valor sem sinal, sentido no tipo

Permitir crédito negativo criaria dois jeitos de escrever um débito, e um relatório que
somasse por tipo daria dois resultados diferentes. O que diz se o dinheiro entra ou sai é o
`Tipo`; a resposta HTTP traz também um campo `Efeito`, com sinal, porque quem consome
costuma querer somar uma lista.

### Número de conta com dígito verificador

Número de conta é digitado à mão numa transferência, e um dígito trocado sem verificação
acha outra conta que existe e manda dinheiro para o desconhecido certo. O verificador é
módulo 11 com pesos crescentes — peso fixo pegaria o dígito trocado, mas não a transposição
de dois vizinhos, que é o segundo erro de digitação mais comum. Há teste para os dois casos,
varrendo todas as adulterações de um dígito.

A parte sequencial vem de uma sequência do banco, não da aplicação: dois processos gerando
número ao mesmo tempo gerariam o mesmo, e o índice único só avisaria depois de um cliente já
ter recebido a resposta.

### A transação é explícita no caso de uso

Não está escondida atrás de um `Salvar` do repositório. Neste sistema a transação é regra de
negócio — transferência debita uma conta e credita outra, e ou as duas linhas entram ou
nenhuma entra. Um repositório que salvasse sozinho tiraria de quem lê o caso de uso a
informação de onde a transação começa e termina. E a trava depende disso: ela vale até o
commit, então quem abre a transação define até quando a linha fica segura.

### Dois trechos de SQL escrito à mão

O resto passa pelo EF, parametrizado. Duas exceções, as duas por imposição do SQL Server:

- **`UPDLOCK`** é uma dica de tabela e não existe no LINQ. O parâmetro entra pela
  interpolação do `FromSql`, que o EF transforma em parâmetro de verdade — não há
  concatenação com valor vindo de fora. As colunas são listadas uma a uma de propósito: com
  `SELECT *`, uma coluna nova no modelo que ainda não existisse na tabela passaria
  despercebida até a primeira leitura em produção.
- **`NEXT VALUE FOR`** é proibido dentro de subconsulta, e `SqlQuery` embrulha o texto numa
  para poder compor LINQ em cima. Custou um 500 até eu descobrir. A saída foi um comando
  cru, com a transação corrente amarrada à mão — sem isso o número sairia mesmo que a
  abertura da conta fosse desfeita, e sequência não volta atrás.

### Abrir conta não exige chave de idempotência

Abertura não move dinheiro, e duas aberturas repetidas dão duas contas vazias — chato, e não
um prejuízo. Exigir chave ali seria cerimônia sem risco por trás.

### A validação de borda repete o domínio de propósito

Na borda o objetivo é dizer ao cliente qual campo está errado e por quê, num formato que ele
mostra na tela. No domínio é impedir que o lançamento exista, venha de onde vier.

## Segurança

- Nenhum valor monetário como `double`. O tipo `Dinheiro` é a única porta de entrada, e ele
  recusa negativo e mais de duas casas decimais.
- Toda operação financeira exige `Idempotency-Key`, e a chave fica gravada.
- Toda operação financeira exige `X-Operador`, gravado em cada lançamento — quem fez e
  quando ficam no ledger, não só no log.
- Ledger é append-only: não existe caminho de alteração nem de exclusão, e a chave
  estrangeira é `Restrict` — conta encerrada continua tendo extrato.
- Saldo insuficiente é 409 e não 400: o pedido está bem formado, o que impede é o estado da
  conta.
- Toda entrada validada no servidor com FluentValidation, e de novo no agregado.
- Segredos em `.env`, fora do git, com `.env.example` versionado.
- HSTS e redirecionamento de HTTPS fora de desenvolvimento; `nosniff`, `no-referrer` e
  `DENY` de enquadramento em toda resposta.
- O registro de requisição fica por fora do tratador de erros, para enxergar o status final:
  por dentro, uma conta inexistente apareceria no log como 500 com pilha inteira em vez do
  404 que o cliente recebeu.

## O que ainda não está aqui

- **Transferência entre contas** — a fatia 2. É onde a ordem de aquisição de trava passa a
  importar.
- **Bloqueio e encerramento de conta** — a fatia 3. Hoje toda conta aceita movimentação.
- **Extrato paginado com filtro de período** — a fatia 4. O índice
  `(ContaId, CriadoEm)` já está criado esperando por ele.
- **Integração com o Core de Crédito** — a fatia 5. O empréstimo vira produto da conta:
  contratar credita o cliente, cada parcela paga debita.
- **Autenticação e papéis.** Enquanto não existirem, `X-Operador` é um substituto explícito —
  ele identifica quem diz ser, e ninguém confere.
- **Estorno.** O ledger já suporta (é uma linha em sentido contrário), mas não há rota.
- **Reconciliação em lote.** Hoje a conferência é conta a conta, sob demanda. Uma varredura
  periódica de toda a base é outro problema — precisa de janela, de paginação e de um lugar
  para reportar.
- **Limite por IP nas rotas de movimentação.**
- Backup do banco: ainda não documentado.
