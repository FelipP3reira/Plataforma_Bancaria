# Plataforma Bancária

Uma fatia de core banking: contas, ledger, saldo e extrato. O ponto não é o CRUD de conta —
é o que um sistema que guarda dinheiro precisa ter para não perder nem inventar nenhum:
lançamento imutável, saldo que se prova contra o ledger, e duas requisições simultâneas
sacando da mesma conta terminando do único jeito certo.

## Estado atual

Completo: conta, ledger, movimentação, transferência atômica, bloqueio, encerramento,
extrato paginado e o Core de Crédito usando a conta como produto.

```
POST /contas                        abre conta (nasce zerada e ativa)
GET  /contas/{id}                   estado, saldo, titular e quantidade de lançamentos
POST /contas/{id}/depositos         credita (exige Idempotency-Key e X-Operador)
POST /contas/{id}/saques            debita, recusando se não houver saldo
POST /contas/{id}/transferencias    debita esta conta e credita outra, atomicamente
GET  /contas/{id}/extrato           paginado por marcador, com filtro de período
GET  /contas/{id}/conciliacao       confere o saldo materializado contra o ledger

POST /contas/{id}/bloqueio          impede movimentação, preservando a consulta
POST /contas/{id}/desbloqueio       volta a movimentar
POST /contas/{id}/encerramento      encerra, exigindo saldo zero
GET  /contas/{id}/estados           a trilha de mudanças de estado
GET  /transferencias/{id}           uma transferência pelo id
```

Há uma **interface web** em `web/`: conta, extrato, transferência, empréstimo, bloqueio e
a conciliação do ledger. E, em desenvolvimento, documentação navegável da API em **`/docs`**
— fora de desenvolvimento ela não sobe: o contrato da API não é segredo, mas uma interface
que dispara requisição de verdade não precisa estar exposta no servidor que guarda dinheiro.

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

O SQL Server sobe na porta **1434**, e não na 1433. O [Core de Crédito](https://github.com/FelipP3reira/Core_Credito)
roda o próprio banco na porta padrão, e os dois sobem juntos quando se quer ver a
integração funcionando.

### A interface

Precisa de Node 20+. Com as duas APIs no ar:

```bash
cd web
npm install
npm run semear   # contas com movimento, transferências e um empréstimo em andamento
npm run dev      # http://localhost:5173
```

`npm run semear` imprime os ids das contas criadas — é com eles que se entra no app.
Sem ele o banco sobe vazio e não há o que olhar.

A origem `http://localhost:5173` precisa estar em `Web__Origens__0` nos `.env` **das duas
APIs**, senão o navegador barra as chamadas antes de elas saírem.

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
abrir uma. E a transferência trava duas contas na mesma operação — o que exige ordem de
aquisição fixa, tratada na seção seguinte.

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

Com a trava, 5 dos 6 testes de concorrência passam; sem ela, os mesmos 5 falham. O sexto —
saques em contas diferentes não disputam entre si — passa nos dois casos, e é isso mesmo que
tem que acontecer.

## A transferência

Um débito na origem e um crédito no destino, dentro de uma transação só. Ou as duas linhas
entram, ou nenhuma entra.

### Débito antes do crédito

O débito é o único dos dois que pode ser recusado, e ele acontece primeiro. Creditar antes
deixaria o destino com dinheiro por um instante, e o tratamento de "não tinha saldo" viraria
um desfazimento em vez de uma recusa. Tem teste de unidade que troca a ordem e verifica que
o destino não encostou no dinheiro.

Os dois lançamentos saem de um método só, no domínio. Se a montagem morasse no caso de uso,
nada impediria um caminho novo de creditar valor diferente do que debitou.

### Ordem de aquisição de trava

É a parte que faz a trava pessimista funcionar em vez de travar o sistema sozinho.

Uma transferência de A para B trava A e depois B. Uma de B para A, ao mesmo tempo, travaria
B e depois A — e cada uma ficaria esperando a trava que a outra já tem. O SQL Server detecta,
mata uma como vítima de deadlock, e uma operação perfeitamente correta vira erro 500.

As contas são travadas **sempre na ordem do id**. Todo mundo pega as travas na mesma
sequência, então quem chega depois espera na primeira e não no meio. A ordem em si não
precisa significar nada — precisa só ser a mesma em todo processo, e a comparação de `Guid`
no .NET é determinista e igual em qualquer máquina.

Isso não é teoria. Dois testes disparam vinte pares cruzados de `A→B` com `B→A`, e um ciclo
de três contas `A→B→C→A`, tudo ao mesmo tempo. **Com a ordem, passam em 1 segundo; sem ela,
os dois falham e a suíte leva 17 segundos** — o tempo que o banco gasta detectando os
deadlocks.

### A chave do cliente não desce para as pernas

A chave de idempotência vale por conta, e a perna de crédito cai numa conta que nunca viu
essa chave. Gravá-la lá faria uma operação qualquer do dono do destino, que por acaso usasse
a mesma chave, colidir com uma transferência que não tem nada a ver com ele.

As pernas carregam uma chave derivada do id da transferência —
`transferencia:{id}:debito` e `:credito` —, que nunca colide. A chave do cliente fica no
registro da transferência, única dentro da conta de origem.

### Por que a transferência é um registro próprio

Poderia ser só dois lançamentos parecidos ligados por um id. Vira tabela por duas razões: a
chave de idempotência é uma só para a operação inteira e precisa de um lugar onde caiba uma
vez, não duas; e perguntar "o que aconteceu com esta transferência" não pode depender de
adivinhar quais dois lançamentos, em contas diferentes, formavam o par.

## Estado da conta

```
Ativa ⇄ Bloqueada
  │         │
  └────┬────┘
       ▼
   Encerrada
```

A tabela de transições é explícita, e a ausência de uma aresta é proibição, não omissão. Com
condicionais espalhadas pelos métodos, "a conta encerrada pode voltar?" viraria uma pergunta
que só o depurador responde. Tem teste que percorre os nove pares do produto cartesiano
contra uma cópia da tabela escrita à parte — divergência entre as duas versões acusa erro de
digitação na real.

### Bloqueio impede movimentar, não consultar

Bloquear não mexe no ledger nem no saldo: a dívida e o histórico continuam exatamente onde
estavam, e o extrato continua consultável. O que muda é só a permissão de gravar linha nova.

**Crédito também é barrado**, e não só débito. Conta sob investigação que ainda recebe
dinheiro vira caixa de passagem justamente enquanto está sendo investigada. Efeito colateral
útil: isso barra transferência com destino bloqueado sem que a transferência precise saber
que existe bloqueio — a conferência mora no agregado, no caminho de todo lançamento.

Numa conta bloqueada e sem dinheiro, a recusa é do bloqueio e não do saldo. A ordem das
conferências é deliberada: o bloqueio é o problema real, e o saldo é consequência de nada.

### Encerrar exige saldo zero

Encerrar com saldo deixaria dinheiro sem dono numa conta que ninguém mais movimenta. Quem
quer fechar tira o dinheiro antes — e essa saída vira lançamento, como qualquer outra.

`Encerrada` é terminal. Reabrir apagaria o motivo do encerramento; quem quiser voltar a ter
conta abre outra, e as duas histórias ficam separadas.

### A trilha de estados é append-only, como o ledger

Bloqueio por suspeita é um evento que alguém vai ter que explicar depois. Guardar só o estado
atual responderia "está bloqueada" sem responder desde quando, por quê, nem a mando de quem —
então cada mudança grava uma linha com motivo, operador e instante, e motivo é obrigatório.

A trilha é ordenada por sequência, e não por data: bloquear e desbloquear na mesma requisição
gravariam o mesmo instante, e a ordem sairia indefinida justamente onde precisa ser lida como
sequência.

Mudar estado passa pela **mesma trava** das movimentações. Bloquear enquanto um saque está no
meio do caminho é exatamente o momento em que o bloqueio precisa funcionar: sem a trava, o
saque leria a conta ativa, o bloqueio gravaria, e o saque gravaria depois — dinheiro saindo
de uma conta que já estava bloqueada.

Não há chave de idempotência aqui: a operação já é idempotente por construção. Bloquear duas
vezes, a segunda é recusada pela máquina de estados, porque `Bloqueada` não vai para
`Bloqueada`.

### Desbloqueio é POST, não DELETE do bloqueio

`DELETE /contas/{id}/bloqueio` seria mais expressivo, e foi a primeira versão. Mas
desbloquear exige motivo — quem liberou uma conta bloqueada por suspeita precisa explicar
tanto quanto quem bloqueou —, e corpo obrigatório em `DELETE` é frágil: o próprio ASP.NET
Core recusa inferir corpo nesse verbo, e proxies costumam descartá-lo.

## O extrato

Ordenado do mais recente para o mais antigo, filtrado por período, paginado por marcador.

```
GET /contas/{id}/extrato?de=2026-09-01T00:00:00Z&ate=2026-09-30T23:59:59Z&tamanho=50
```

A resposta traz as linhas e um `proximaPagina` — um texto opaco que o cliente devolve para
continuar de onde parou. Quando ele vem nulo, acabou.

### Marcador, e não `OFFSET`

Extrato é uma lista que cresce por cima. Com `OFFSET`, um lançamento novo entre duas
páginas empurra tudo para baixo e o cliente vê a mesma linha duas vezes — sem erro, sem
aviso. O marcador ancora na linha, e não na contagem.

O outro motivo é custo: `OFFSET 10000` faz o banco ler dez mil linhas para descartá-las. O
marcador faz uma busca direta, e a página quinhentos custa o mesmo que a primeira.

### O marcador tem duas colunas, e as duas são necessárias

```
(CriadoEm, Sequencia)
```

`CriadoEm` sozinho repete: o instante vem do relógio no começo do pedido, e dois lançamentos
na mesma conta podem cair no mesmo tick. `Sequencia` sozinha não serve porque não é ela que
ordena o extrato — a ordem é cronológica, e a sequência só desempata. Juntas formam ordem
total, porque a sequência é única dentro da conta.

O predicado sai escrito para o índice: a comparação principal em `CriadoEm`, e a sequência
só no empate.

```sql
CriadoEm < @instante OR (CriadoEm = @instante AND Sequencia < @sequencia)
```

### O índice ganhou uma terceira coluna

```
IX_Lancamentos_ContaId_CriadoEm_Sequencia
```

A sequência entra como **coluna-chave**, e não como coluna incluída. Como coluna incluída
ela seria lida, mas não ordenada, e cada página custaria uma ordenação do período inteiro.
Sendo chave, a ordem do índice é a ordem do extrato e o banco continua a busca de onde
parou.

### Não há total de linhas, de propósito

Contar o período inteiro é justamente a consulta que fica cara quando a conta tem milhares
de lançamentos — e seria paga em toda página, para mostrar um número que muda entre uma
página e outra. Em troca, a consulta pede `tamanho + 1` linhas: se vier a linha extra, há
próxima página. Uma linha a mais resolve o que um `COUNT` custaria.

### O saldo corrente não é recalculado

Cada linha traz o `SaldoDepois` que foi gravado no momento do lançamento. O extrato mostra
saldo corrente sem depender de o cliente ter pedido as páginas anteriores, e sem somar nada.

### Tamanho fora da faixa é recusado, não aparado

Cliente que pede 5.000 e recebe 200 sem aviso conclui que a conta só tem 200 lançamentos no
período. O mesmo vale para marcador de outro período: paginar com ele andaria em cima de um
recorte diferente do que gerou o marcador.

### Conta bloqueada e conta encerrada continuam com extrato

É o outro lado do bloqueio. Cortar o extrato tiraria a informação exatamente de quem precisa
apurar a suspeita, e conta encerrada continua sendo auditável — a obrigação de guardar o
histórico não acaba quando o cliente vai embora.

## O empréstimo como produto da conta

O [Core de Crédito](https://github.com/FelipP3reira/Core_Credito) analisa e concede; esta
plataforma guarda o dinheiro. Contratado um empréstimo com uma conta daqui informada, ele
vira movimentação normal desta conta:

```
desembolso        ->  POST /contas/{id}/depositos    +10.000,00
parcela 1 paga    ->  POST /contas/{id}/saques        -1.779,24
```

Do lado de cá **nada foi adicionado para isso funcionar**. O crédito usa as mesmas rotas de
depósito e saque que qualquer cliente usa, com o mesmo `Idempotency-Key` e o mesmo
`X-Operador` — que aparece no extrato como `credito:desembolso` e `credito:pagamento`, e é
o que permite auditar depois quem mandou movimentar.

Isso é resultado do desenho, e não sorte: a idempotência mora na conta e vale para qualquer
chamador, então o crédito consegue repetir um desembolso interrompido sem creditar duas
vezes. A chave que ele apresenta é derivada do contrato, e o raciocínio inteiro está
documentado do lado de lá.

O que a plataforma faz questão de continuar fazendo: se a conta não tem saldo para a
parcela, o saque é recusado com 409 e o crédito trata isso como recusa — a parcela fica em
aberto. Conta bloqueada recusa desembolso e cobrança do mesmo jeito, porque a verificação
mora no agregado, no caminho de todo lançamento, e não em cada chamador.

## A interface

React + TypeScript + Vite + Tailwind, em `web/`. Quatro abas: **Conta** (saldo e
movimentação), **Extrato** (paginado, com filtro de período), **Empréstimos** e
**Segurança** (bloqueio, trilha de estados e conciliação).

### A chave de idempotência nasce com o formulário, não com o clique

É o detalhe que faz o cabeçalho valer alguma coisa. A chave é gerada quando o formulário
abre e **sobrevive ao erro**: se a primeira tentativa falhar por rede, a segunda vai com a
mesma chave, e a API devolve o lançamento que já existe em vez de criar um segundo.

Gerada dentro do clique, dois cliques virariam dois depósitos — exatamente o problema que
`Idempotency-Key` existe para impedir. A tela mostra os primeiros caracteres da chave
enquanto o formulário está aberto, para o comportamento ficar visível em vez de implícito.

O mesmo vale para o pagamento de parcela: a chave é guardada por número de parcela, em um
`useRef`, e reaproveitada entre tentativas.

### O saldo é sempre relido do servidor

Nenhuma tela ajusta o saldo em memória depois de uma operação — todas releem a conta. Saldo
calculado no cliente diverge do ledger no primeiro caso que a interface não previu, e o
saldo é justamente o número que não pode estar errado aqui.

### A tela de entrada não é login, e diz isso

Não há autenticação nas APIs. Quem sabe o id de uma conta entra nela. A tela avisa isso em
texto, e o `X-Operador` sai como `web:<titular>` — no extrato dá para separar o que veio da
interface do que veio do Core de Crédito (`credito:desembolso`) ou de um operador interno.

Uma tela de senha que não validasse nada seria pior do que não ter: passaria a impressão de
que valida.

### A interface fala com as duas APIs, e o banco não chama o crédito

A junção entre os dois serviços acontece **na borda de apresentação**. A dependência entre
eles continua de mão única — crédito → banco —, e fazer o banco chamar o crédito para
montar a aba de empréstimo criaria um ciclo entre os dois.

Um BFF no meio seria a alternativa de manual. Não está aqui porque a única coisa que ele
faria hoje é repassar duas chamadas, e uma peça a mais no caminho do dinheiro precisa
pagar por si.

### Recusa e falha não têm a mesma cara

4xx é decisão da API — saldo insuficiente, conta bloqueada — e insistir não muda nada;
o aviso é âmbar e mostra o motivo que veio no `ProblemDetails`. 5xx e API fora do ar são
falha, aparecem em vermelho, e aí repetir faz sentido. Mostrar as duas iguais faria a
pessoa insistir num pedido que nunca vai passar.

### Os dados de demonstração entram pela porta da frente

`npm run semear` cria as contas chamando as APIs como qualquer cliente, e não com `INSERT`
no banco. Dado semeado por fora do domínio nasceria sem passar pela máquina de estados nem
pelo ledger, e o app mostraria um saldo que a própria tela de conciliação recusaria.

O que a semeadura **não** faz é espalhar os lançamentos no tempo: todos ficam com a data de
hoje. O instante vem do relógio do servidor no momento do pedido, e não do corpo da
requisição — deixar o cliente escolher a data de um lançamento seria abrir a porta para
forjar extrato.

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
- Ledger e trilha de estados são append-only: não existe caminho de alteração nem de
  exclusão, e a chave estrangeira é `Restrict` — conta encerrada continua tendo extrato.
- Bloqueio barra crédito e débito, inclusive transferência entrando. Conta sob investigação
  não vira caixa de passagem.
- Mudança de estado exige motivo e operador, e grava os dois. Bloqueio sem motivo não se
  audita.
- Saldo insuficiente é 409 e não 400: o pedido está bem formado, o que impede é o estado da
  conta.
- Toda entrada validada no servidor com FluentValidation, e de novo no agregado.
- Segredos em `.env`, fora do git, com `.env.example` versionado.
- HSTS e redirecionamento de HTTPS fora de desenvolvimento; `nosniff`, `no-referrer` e
  `DENY` de enquadramento em toda resposta.
- A documentação navegável (`/docs`) só sobe em desenvolvimento.
- O extrato não devolve a chave de idempotência do lançamento: ela é apresentada por quem
  movimenta e não faz parte do que o cliente precisa ver.
- O registro de requisição fica por fora do tratador de erros, para enxergar o status final:
  por dentro, uma conta inexistente apareceria no log como 500 com pilha inteira em vez do
  404 que o cliente recebeu.

## O que ainda não está aqui

- **Autenticação e papéis.** É a maior lacuna, e a interface a torna visível: a tela de
  entrada pede o id da conta e pronto. Enquanto não existirem, `X-Operador` é um substituto explícito —
  ele identifica quem diz ser, e ninguém confere.
- **Estorno.** O ledger já suporta (é uma linha em sentido contrário), mas não há rota.
- **Reconciliação em lote.** Hoje a conferência é conta a conta, sob demanda. Uma varredura
  periódica de toda a base é outro problema — precisa de janela, de paginação e de um lugar
  para reportar.
- **Limite por IP nas rotas de movimentação.**
- **Filtro do extrato por tipo, valor ou origem.** Hoje o recorte é só por período.
- **Extrato em arquivo** (CSV, PDF). Hoje sai só como JSON, pela rota.
- **Testes da interface.** O back-end tem 167; a web não tem nenhum. O fluxo foi conferido
  ponta a ponta com as duas APIs no ar, mas isso é conferência manual, não suíte.
- **Responsividade em telas pequenas.** A tabela do extrato rola na horizontal e resolve,
  mas não é um layout pensado para celular.
- Backup do banco: ainda não documentado.
