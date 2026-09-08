---
id: MER-001
title: Corrigir os 15 achados do code review inicial (segurança, concorrência, config, frontend)
status: 🟡 Planejada
repo: meridian
scope: fullstack
created_at: 2026-09-08
related: []
issues: [1]
blocked_by: []
blocks: []
severity: high
---

## Contexto

O code review completo do commit inicial (15 achados verificados, 14 confirmados +
1 plausível) mostrou um padrão: o caminho feliz está sólido (43 testes verdes, smoke
E2E ok), mas **os caminhos concorrentes e de configuração não receberam o mesmo
rigor**. Nenhum achado quebra a contabilidade em condição normal — o rollback sempre
protege o dinheiro — mas quatro são críticos (token forjável, ledger divergente,
contas de sistema duplicadas, violação de regra Blocker do CLAUDE.md) e vários
quebram o fluxo dev documentado.

## Estado atual

Código do commit inicial (`main`). Referências por achado (arquivo:linha na época do
review):

| # | Onde | Achado |
|---|---|---|
| 1 | `server/src/Meridian.Api/Security/JwtOptions.cs:8` | Chave JWT default commitada; sem fail-fast fora de Development |
| 2 | `server/src/Meridian.Domain/Entities/Transfer.cs:26` | Sem guard de escala; numeric(18,2) arredonda e ledger diverge |
| 3 | `server/src/Meridian.Application/Services/SystemAccountProvider.cs:24` | find-then-create sem lock; índice (IsSystem, Currency) não é unique |
| 4 | `server/src/Meridian.Application/Services/AuthService.cs:67` | Saldo de abertura fora de `ConcurrencyRetry` (Blocker CLAUDE.md) |
| 5 | `server/src/Meridian.Api/Filters/IdempotencyFilter.cs:57` | check-then-act: requests concorrentes com mesma key → 500 |
| 6 | `server/src/Meridian.Infrastructure/Messaging/RabbitMqEventPublisher.cs:38` | Sem publisher confirms; outbox marca processado sem garantia |
| 7 | `server/src/Meridian.Infrastructure/DependencyInjection.cs:55` | Conexão SQLite singleton compartilhada entre requests concorrentes |
| 8 | `server/src/Meridian.Api/Filters/IdempotencyFilter.cs:86` | Commit só em `ObjectResult` 2xx; `NoContent()` faria rollback silencioso |
| 9 | `server/src/Meridian.Api/appsettings.json:26` | guest/guest não autentica no broker do compose (meridian/meridian) |
| 10 | `web/src/app/features/dashboard/dashboard-page.ts:77` | `list()` sem accountId → 400 sempre; widget nunca carrega |
| 11 | `server/src/Meridian.Application/Services/AuthService.cs:56` | Email duplicado concorrente → DbUpdateException crua → 500 |
| 12 | `server/src/Meridian.Api/Filters/IdempotencyFilter.cs:53` | Key >200 chars → 500 no commit após a ação executar |
| 13 | `server/src/Meridian.Infrastructure/DependencyInjection.cs:88` | Migrate() engolido + `/health` 200 com DB fora |
| 14 | `server/Dockerfile:10` | Sem `.dockerignore`; obj/ do host quebra `docker compose up --build` |
| 15 | `web/src/app/core/accounts.service.ts:12` | Signal de contas não limpo no logout; dados do usuário anterior vazam |

## Decisões já fechadas

| # | Decisão |
|---|---|
| D1 | Escala monetária: **rejeitar** no domínio valor com mais de 2 casas decimais (`DomainValidationException` → 422 `invalid-amount-scale`). Não arredondar silenciosamente — ledger não adivinha intenção. |
| D2 | Contas de sistema: **índice unique filtrado** em (Currency) WHERE IsSystem + captura da violação no `GetOrCreateAsync` com re-fetch (perdedor usa a conta do vencedor). |
| D3 | Idempotência concorrente: **insert de reserva** do `IdempotencyRecord` (sem response) antes de executar a action; violação do unique no insert → aguardar/retornar 409 `idempotency-in-flight` com `Retry-After`. Registro completo é atualizado no commit. |
| D4 | Chave JWT: fora de `Development`, se `Jwt:Key` ausente ou igual ao default do repo → **falhar o boot** com mensagem clara. |
| D5 | RabbitMQ: padronizar **meridian/meridian** em appsettings e compose (dev local = compose broker). |
| D6 | Publisher confirms: `ConfirmSelect` + `WaitForConfirms` com timeout; `TryPublish` só retorna true confirmado. |
| D7 | Transferências recentes do dashboard: **backend passa a aceitar** `GET /api/transfers` sem `accountId`, retornando transfers de todas as contas do usuário autenticado (paginado). Frontend mantém a chamada atual. |
| D8 | `/health`: DB inacessível → **503** com `database: "unavailable"` (compose healthcheck e probes passam a refletir a verdade). Migração falhando no boot continua não-fatal em Development, mas o health expõe. |
| D9 | Achado 8 (plausível): commit do `IdempotencyFilter` passa a cobrir **qualquer** `StatusCode` 2xx (incluindo `StatusCodeResult`/`NoContent`); response body ausente é serializado como vazio no registro. |
| D10 | SQLite singleton: modo Sqlite é para **testes e demo single-user**; documentar a limitação no CLAUDE.md e serializar o acesso (connection por scope com `Cache=Shared` in-memory nomeado) — sem engenharia pesada. |

## 1. Críticos

- **JWT (D4)**: validação no boot em `Program.cs`/`JwtOptions`; remover fallback
  silencioso `new JwtOptions()`.
- **Escala (D1)**: guard em `Transfer.Execute` (e por consequência deposit); mapear
  exceção nova no middleware.
- **Conta de sistema (D2)**: migration nova com unique filtrado + tratamento de
  corrida no provider.
- **Registro (achado 4)**: envolver a transferência de abertura em
  `ConcurrencyRetry.ExecuteAsync`, igual aos demais services.

## 2. Concorrência backend

- **Idempotência (D3 + achados 5/12)**: reserva + guard de tamanho da key (≤200 →
  senão 422 `invalid-idempotency-key`) **antes** de executar a action.
- **Email duplicado (achado 11)**: capturar violação do unique no `SaveChanges` e
  mapear pra 409 (mesmo shape do caminho sequencial).
- **Commit do filtro (D9)**.

## 3. Config / infra

- **RabbitMQ creds (D5)** + **publisher confirms (D6)**.
- **Health/migração (D8)**: `/health` honesto; compose `depends_on` da web pode usar
  healthcheck da api.
- **`.dockerignore` (achado 14)**: `**/bin`, `**/obj` no `server/`; conferir
  `web/.dockerignore` existente.
- **SQLite (D10)**.

## 4. Frontend

- **Transferências recentes (D7)**: nenhuma mudança de chamada; validar que o widget
  renderiza com o backend novo.
- **Logout (achado 15)**: `auth.service.logout()` limpa os signals de estado
  (`accountsSignal` e equivalentes) — expor `reset()` nos services.

## Critérios de aceitação

- [x] Boot fora de Development com `Jwt:Key` ausente/default falha com erro claro
- [x] `POST /api/transfers` com `amount: 10.005` → 422 `invalid-amount-scale`
- [x] N registros/depósitos concorrentes numa moeda nova criam **uma** conta de sistema (teste de corrida)
- [x] Registro concorrente com movimentação na conta de sistema não retorna 409 (retry funciona)
- [x] Duas requests concorrentes com a mesma `Idempotency-Key`: uma executa, a outra recebe replay ou 409 `idempotency-in-flight` — nunca 500, nunca dupla execução
- [x] `Idempotency-Key` com 250 chars → 422 antes de executar a action
- [x] Dois `register` concorrentes do mesmo email → um 201, um 409
- [x] `TryPublish` com broker fora retorna false e a linha do outbox permanece não processada (Attempts incrementa)
- [x] `GET /api/transfers` sem `accountId` → 200 paginado com transfers de todas as contas do usuário
- [x] Dashboard renderiza "Transferências recentes" sem snackbar de erro *(frontend já
  chamava `list()` sem `accountId`; backend agora responde 200 — ver seção de
  implementação frontend)*
- [x] Logout limpa contas em memória; login de outro usuário não exibe dados do anterior
  *(`AccountsService.reset()` chamado em `AuthService.logout()`; spec de service fica
  para o `test-agent` — ver seção de implementação frontend)*
- [x] `/health` → 503 com Postgres fora; 200 com DB ok
- [x] `docker compose up --build` funciona após `dotnet build` local (com `.dockerignore`)
- [ ] `dotnet run` + `docker compose up postgres rabbitmq -d` publica evento no exchange `meridian.events` (validável na management UI) *(não executado neste ambiente — sem RabbitMQ local disponível; ver seção de implementação)*
- [x] Suítes backend e frontend verdes; baseline não regride *(backend: 43/43 verde,
  zero warnings; frontend: `npm run build` limpo, `npm test -- --no-watch` 8/8 verde
  — 3 test files, mesma baseline de antes; testes novos da task ficam para o
  test-agent)*

## Não faz parte do escopo

- Cauda de limpeza do review (bloco de persistência triplicado, contracts de response
  ausentes, `TransferStatus.Failed` morto, guard de ownership duplicado, índices
  compostos em `Transfers`, memoização do `isAuthenticated`, CI sem `npm test`) —
  vira MER-002.
- **Achado novo (code review pós-D7):** coluna "Origem" do dashboard mostra fragmento
  de GUID cru (ex.: `a1b2c3d4…`) em vez de um nome quando a conta de origem é a conta
  de sistema (todo depósito). `nameFor()` em `dashboard-page.ts` cai no fallback
  porque contas de sistema (`OwnerUserId = null`) nunca vêm em `GET /api/accounts` —
  só ficou visível agora que o widget carrega de verdade (achado 10 fixado; antes o
  endpoint sempre respondia 400 e a tabela nunca renderizava linhas). Não é regressão
  desta task nem um dos 15 achados originais — vira MER-002 (provável fix: nome fixo
  tipo "Depósito"/"Sistema" no frontend quando `sourceAccountId` não está no mapa de
  contas do usuário, ou o backend expor um nome de exibição pra conta de sistema).
- Itens do roadmap do README (webhooks, multi-moeda, k6).

## Branch e merge

- Branch: `task/MER-001-code-review-fixes` a partir de `main`
- Merge: PR contra `main`; release patch (`v0.1.x`) via `/ship`

## Estimativa

- 2–3 sessões; ~20 arquivos tocados (4 domínio/application, 4 api, 3 infra,
  1 migration, 2 compose/docker, 3 web, testes novos)

## Riscos monitorados

| Risco | Mitigação |
|---|---|
| Índice unique filtrado em (Currency) WHERE IsSystem se comportar diferente no SQLite dos testes | Testar a corrida via unit no provider + integração no Postgres manualmente; manter o re-fetch como caminho principal |
| Reserva de idempotência introduzir deadlock com a transação do filtro | Reserva em transação curta separada, antes da transação da action |
| Publisher confirms degradarem throughput do outbox | Confirmar por batch (`WaitForConfirms` após o lote), não por mensagem |
| Mudança do `/health` pra 503 derrubar o compose (`depends_on` da web) | Ajustar healthcheck do compose junto; smoke com `docker compose up` completo |

## Implementação Backend

**Concluída em:** 2026-09-08
**Branch:** `task/MER-001-code-review-fixes`

Escopo desta entrega: achados **1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13, 14** e a metade
backend do achado 10 (D7). Achados 10 (frontend) e 15 ficam para o `web-agent`.

### Arquivos criados

- `server/.dockerignore` (achado 14)
- `server/src/Meridian.Infrastructure/Migrations/20260908130903_SystemAccountFilteredIndexAndIdempotencyReservation.cs`
- `server/src/Meridian.Infrastructure/Migrations/20260908130903_SystemAccountFilteredIndexAndIdempotencyReservation.Designer.cs`

### Arquivos modificados

- `CLAUDE.md` — documenta a limitação do modo Sqlite (D10) e corrige o comando
  `--no-launch-profile` do fluxo "sem docker" (ver "Decisões fora de D1–D10" abaixo).
- `docker-compose.yml` — healthcheck do `api` (curl em `/health`); `web` passa a
  depender de `api` com `condition: service_healthy` (achado 13/D8).
- `server/Dockerfile` — instala `curl` na imagem runtime pro healthcheck do compose.
- `server/src/Meridian.Api/Security/JwtOptions.cs` — `EnsureConfiguredFor(bool)`.
- `server/src/Meridian.Api/Program.cs` — chama a validação no boot; `/health` honesto
  (503 quando o DB está inacessível).
- `server/src/Meridian.Api/appsettings.json` — RabbitMq `meridian/meridian` (D5) +
  `ConfirmTimeoutSeconds`.
- `server/src/Meridian.Api/Middleware/ExceptionHandlingMiddleware.cs` — mapeia
  `InvalidAmountScaleException` → 422 `invalid-amount-scale`.
- `server/src/Meridian.Api/Filters/IdempotencyFilter.cs` — reescrito: reserva antes de
  executar (D3), guard de tamanho de key ≤200 → 422 `invalid-idempotency-key` (achado
  12), 409 `idempotency-in-flight` com `Retry-After` em corrida (achados 5/D3), commit
  cobre qualquer `IStatusCodeActionResult` 2xx (D9/achado 8), remove a reserva quando a
  action falha (mantém o retry-após-falha que já existia).
- `server/src/Meridian.Api/Controllers/TransfersController.cs` — `accountId` vira
  `Guid?` opcional (D7).
- `server/src/Meridian.Domain/Exceptions/DomainExceptions.cs` — `InvalidAmountScaleException`.
- `server/src/Meridian.Domain/Entities/Transfer.cs` — guard de escala em `Execute` (D1).
- `server/src/Meridian.Domain/Entities/IdempotencyRecord.cs` — vira `Reserve`/`Complete`
  (`ResponseStatusCode`/`ResponseBody` nullable, `CompletedAt`, `IsCompleted`).
- `server/src/Meridian.Application/Exceptions/ApplicationExceptions.cs` —
  `ConflictException(string, Exception)`.
- `server/src/Meridian.Application/Abstractions/Repositories.cs` —
  `IIdempotencyRepository.Remove`; `ITransferRepository.GetPageByOwnerAsync`.
- `server/src/Meridian.Application/Services/SystemAccountProvider.cs` — cria a conta de
  sistema com save próprio; violação do índice único → `ClearTracking` + relança como
  `ConcurrencyConflictException` (D2), pra reaproveitar o `ConcurrencyRetry` do chamador.
  Expõe `ContentionMaxAttempts` (ver decisão fora de D1–D10 abaixo).
- `server/src/Meridian.Application/Services/AuthService.cs` — `RegisterAsync` inteiro
  dentro de `ConcurrencyRetry.ExecuteAsync` (achado 4); `GetOrCreateAsync` é chamado
  antes de qualquer `Add` no change tracker (pra o `ClearTracking` de um conflito não
  derrubar entidades já staged).
- `server/src/Meridian.Application/Services/AccountService.cs` — `DepositAsync` usa
  `maxAttempts: SystemAccountProvider.ContentionMaxAttempts`.
- `server/src/Meridian.Application/Services/TransferService.cs` — `ListAsync` aceita
  `accountId` nulo e delega pra `GetPageByOwnerAsync` (D7).
- `server/src/Meridian.Infrastructure/Persistence/MeridianDbContext.cs` — índice único
  filtrado `IX_Accounts_Currency_IsSystem` em `Currency` `WHERE "IsSystem" = TRUE` (D2);
  `IdempotencyRecord.IsCompleted` ignorado no model.
- `server/src/Meridian.Infrastructure/Persistence/EfUnitOfWork.cs` — violação de unique
  constraint (Postgres `23505` / Sqlite `SQLITE_CONSTRAINT_UNIQUE`/`PRIMARYKEY`) vira
  `ConflictException` em vez de `DbUpdateException` cru (resolve achado 11 e alimenta o
  D2/D3).
- `server/src/Meridian.Infrastructure/Persistence/Repositories/IdempotencyRepository.cs`
  — `Remove`.
- `server/src/Meridian.Infrastructure/Persistence/Repositories/TransferRepository.cs` —
  `GetPageByOwnerAsync` (join em `Accounts.OwnerUserId`, mesma paginação/ordenação de
  `GetPageAsync`).
- `server/src/Meridian.Infrastructure/DependencyInjection.cs` — Sqlite passa a usar
  `Mode=Memory;Cache=Shared` com nome gerado + uma conexão singleton "keep-alive" que só
  segura o banco vivo; cada `MeridianDbContext` abre sua própria conexão (D10/achado 7).
- `server/src/Meridian.Infrastructure/Messaging/RabbitMqEventPublisher.cs` —
  `ConfirmSelect()` no channel + `WaitForConfirms(timeout, out timedOut)` após o
  `BasicPublish`; só retorna `true` confirmado (D6).
- `server/src/Meridian.Infrastructure/Messaging/RabbitMqOptions.cs` —
  `ConfirmTimeoutSeconds` (default 5s).
- `server/src/Meridian.Infrastructure/Migrations/MeridianDbContextModelSnapshot.cs` —
  regenerado pela migration nova.

### Migrations

- `server/src/Meridian.Infrastructure/Migrations/20260908130903_SystemAccountFilteredIndexAndIdempotencyReservation.cs`
  — troca `IX_Accounts_IsSystem_Currency` (não-único) por `IX_Accounts_Currency_IsSystem`
  (único, filtrado `"IsSystem" = TRUE`); `IdempotencyRecords.ResponseStatusCode`/
  `ResponseBody` viram nullable; nova coluna `CompletedAt`. Gerada via
  `dotnet tool restore && dotnet ef migrations add ... --project src/Meridian.Infrastructure
  --startup-project src/Meridian.Api`. Nenhuma migration existente foi editada.

### Contrato final pro frontend — `GET /api/transfers`

- **Método/rota:** `GET /api/transfers` (inalterada)
- **Query params:**
  - `accountId` (`Guid`, **opcional agora**) — se presente, filtra pela conta (dono
    precisa ser o usuário do token, senão 404, comportamento inalterado). Se **ausente**,
    retorna transfers de **todas** as contas do usuário autenticado.
  - `page` (`int?`, default 1), `pageSize` (`int?`, default 20, máx 100) — inalterados.
- **Response 200:** `PagedResult<TransferDto>` — mesmo shape de sempre:
  ```json
  {
    "items": [ { "id": "...", "sourceAccountId": "...", "destinationAccountId": "...",
                 "amount": 50.0, "currency": "BRL", "description": null,
                 "status": "Completed", "createdAt": "..." } ],
    "total": 2, "page": 1, "pageSize": 20
  }
  ```
- **Erros:** 401 sem token (inalterado); 404 se `accountId` for informado e não
  pertencer ao usuário (inalterado). **Removida** a validação antiga que retornava 400
  quando `accountId` estava ausente — esse era exatamente o bug do achado 10.
- **Frontend:** nenhuma mudança de chamada necessária — o dashboard já chama
  `GET /api/transfers` sem `accountId`; passa a receber 200 em vez de 400.

### Decisões tomadas fora de D1–D10

1. **Ordem em `AuthService.RegisterAsync` e orçamento de retry maior pra contenção no
   `SystemAccountProvider`.** `SystemAccountProvider.GetOrCreateAsync` faz o próprio
   `SaveChangesAsync` pra detectar a corrida de criação cedo; se isso rodasse depois de
   `_users.Add`/`_accounts.Add` já estarem no change tracker, o `ClearTracking()` do
   caminho de conflito descartaria essas entidades sem re-adicioná-las, perdendo o
   registro numa retry. Resolvido chamando `GetOrCreateAsync` **antes** de qualquer
   `Add` no `RegisterAsync`. Além disso, testei a corrida real com `Task.WhenAll` (fora
   da suíte, script descartado) e o orçamento padrão de `ConcurrencyRetry`
   (`DefaultMaxAttempts = 3`) não é suficiente pra 5+ registros/depósitos concorrentes
   na mesma moeda nova (a corrida de criação **e** a de débito otimista no mesmo
   `Account.Version` competem pelo mesmo orçamento). Adicionei
   `SystemAccountProvider.ContentionMaxAttempts = 8`, usado só em `AuthService.RegisterAsync`
   e `AccountService.DepositAsync` (as duas chamadas que passam por
   `GetOrCreateAsync`) — não mudei o `DefaultMaxAttempts` global, que continua 3 pra
   `TransferService` e demais usos. Validado local com até 10 registros concorrentes
   (`Task.WhenAll`) de forma repetida sem 409.
2. **Reserva de idempotência é removida (não marcada como "falha") quando a action
   falha.** D3 diz "registro completo é atualizado no commit", mas não define o que
   acontece com a reserva se a transação da action nunca commitar. Optei por
   `_records.Remove` + save após o rollback da transação da action, preservando o
   comportamento pré-existente coberto por
   `IdempotencyTests.FailedRequest_IsNotRecorded_SoRetryCanSucceed` (retry com a mesma
   key após uma falha de negócio continua funcionando). A alternativa — tratar a falha
   como "completa" com o status de erro — quebraria esse teste (ele reusa a mesma key
   com payload diferente na retry) e pareceu contradizer a intenção original de
   idempotência do produto.
3. **`ConflictException` genérica reaproveitada em três pontos.** `EfUnitOfWork`
   traduz **qualquer** violação de unique constraint (Postgres `23505` / Sqlite
   `SQLITE_CONSTRAINT_UNIQUE`/`PRIMARYKEY`) pra `ConflictException`. Isso resolve o
   achado 11 (email duplicado) direto — sem código novo no `AuthService` além do
   reordenamento acima — e também alimenta o D2 (`SystemAccountProvider` recatura e
   relança como `ConcurrencyConflictException`) e o D3 (`IdempotencyFilter` recaptura
   pra responder 409 `idempotency-in-flight`). Ainda mapeia pra 409 `conflict` genérico
   no middleware pra qualquer outro caso futuro não tratado explicitamente.
4. **`Retry-After` fixo em 1 segundo**, sem cálculo dinâmico — D3 não especifica o
   valor; um inteiro fixo pequeno é suficiente pro caso de uso (replay rápido de uma
   ação idempotente).
5. **Índice filtrado usa `HasFilter("\"IsSystem\" = TRUE")` direto no model**, sem
   isolar por provider — validado que Postgres e Sqlite (via `EnsureCreated` nos
   testes, `Microsoft.Data.Sqlite` 8.0.11) aceitam essa sintaxe igualmente; não foi
   necessário nenhum branch por provider.
6. **Corrigi o comando `--no-launch-profile` documentado no `CLAUDE.md`.** Sem launch
   profile, `ASPNETCORE_ENVIRONMENT` não é setado e o host assume `Production` por
   padrão — o que agora dispara o fail-fast do JWT (D4) mesmo em dev local. Adicionei
   `ASPNETCORE_ENVIRONMENT=Development` explícito ao comando documentado. Sem esse
   ajuste, o fluxo "sem docker" documentado no próprio CLAUDE.md ficaria quebrado por
   este mesmo PR.
7. **Risco de Postgres com a reserva do `SystemAccountProvider` dentro da transação
   ambiente do `IdempotencyFilter` (endpoint de depósito).** Como o risco já monitorado
   na task aponta, uma violação de unique constraint é um erro real de SQL no Postgres
   e aborta a transação corrente (diferente do Sqlite, que só reverte a instrução via
   savepoint implícito). Isso significa que, no **Postgres**, uma corrida de criação de
   conta de sistema durante um depósito idempotente (moeda nova, primeiro depósito)
   pode deixar a transação da action inutilizável até o rollback — cenário raro (só na
   primeira vez que uma moeda é usada) mas não coberto por teste automatizado aqui (a
   suíte roda em Sqlite). Não fiz engenharia adicional pra isolar essa reserva numa
   conexão separada porque o próprio risco monitorado da task já aceita esse trade-off
   ("manter o re-fetch como caminho principal" + validação manual no Postgres). Fica
   como item pra validação manual do dev com `docker compose up postgres` antes do
   `/ship`.

### Correção pós code review (Should fix)

O code-reviewer aprovou os achados 1-3,4,6-9,11,13,14 e D5/D7/D9/D10, mas achou um
**Should fix** bloqueante em `IdempotencyFilter.RemoveReservationAsync`: quando a
action de um endpoint `[Idempotent]` esgota o próprio orçamento de `ConcurrencyRetry`
(ex.: `AccountService.DepositAsync`/`AuthService.RegisterAsync` sob contenção real), o
`ConcurrencyConflictException` final sobe **sem** `ClearTracking()` — o retry loop só
limpa o tracker entre tentativas (`onConflict`), não na última falha. De volta no
filtro, `RemoveReservationAsync` tentava `_records.Remove(reservation)` +
`SaveChangesAsync()` com essas entidades órfãs (Version stale) ainda no tracker, o que
falhava de novo com `ConcurrencyConflictException` — dessa vez sem ninguém capturando,
mascarando a exceção original e deixando o `IdempotencyRecord` preso com
`IsCompleted = false` para sempre (qualquer retry futuro com a mesma
`Idempotency-Key` recebia 409 `idempotency-in-flight` eternamente).

**Fix aplicado** em `server/src/Meridian.Api/Filters/IdempotencyFilter.cs`:
`RemoveReservationAsync` agora chama `_unitOfWork.ClearTracking()` **antes** de
`_records.Remove(reservation)` + `SaveChangesAsync()`, garantindo que o delete seja a
única mudança pendente. A chamada inteira também ficou dentro de um `try/catch`: se a
liberação da reserva falhar por qualquer motivo, o erro é só logado
(`ILogger<IdempotencyFilter>`, injetado agora) e engolido — a exceção **original** da
action (já capturada em `executed.Exception`) continua a se propagar normalmente pro
`ExceptionHandlingMiddleware`, sem ser mascarada. Pior caso remanescente: a reserva
fica presa até uma limpeza manual, mas o erro real do cliente não é escondido.

Validado com um teste determinístico descartado (não commitado): duas `MeridianDbContext`
apontando pro mesmo Sqlite shared-cache, uma bate a `Version` de uma conta por baixo da
outra, a segunda fica com uma entidade suja (stale) rastreada e tenta remover a reserva
— sem `ClearTracking()` isso lança `ConcurrencyConflictException` de novo (reproduz o
bug relatado); com `ClearTracking()` antes, o delete sucede e o registro some do banco.
Rodado 5x sem flake.

Nits reportados pelo reviewer, cientes e fora de escopo desta correção: (1)
`TransferRepository.GetPageAsync`/`GetPageByOwnerAsync` compartilham >70% do corpo — a
regra do CLAUDE.md mira privados de service, não bloqueia aqui; (2) `POST /api/auth/register`
tem efeito financeiro mas não é `[Idempotent]` — pré-existente ao commit inicial, fora
do escopo do MER-001, candidato a MER-002.

### Build/test

```
cd server && dotnet build   # 0 Warning(s), 0 Error(s)
cd server && dotnet test    # Passed! 43/43, 0 Failed (baseline preservada)
```

Verificações manuais adicionais (fora da suíte, sem deixar artefato):
- `POST /api/transfers` com `amount: 10.005` → 422 `invalid-amount-scale` (curl contra
  instância local rodando com `Database__Provider=Sqlite`).
- `GET /api/transfers` sem `accountId` → 200 com transfers de todas as contas.
- `docker build ./server` com a imagem final rodando `Database__Provider=Sqlite`:
  `/health` responde 200 e `curl` dentro do container funciona (valida o healthcheck do
  compose). Não rodei `docker compose up --build` completo neste ambiente (porta 5432
  já ocupada por outro Postgres na máquina) — recomendo o dev rodar localmente antes do
  `/ship`.
- Não validei a publicação real no RabbitMQ (`meridian.events` na management UI) —
  este ambiente não tem um broker disponível pra subir via compose sem conflito de
  porta com outros containers já rodando na máquina. A lógica de `ConfirmSelect` +
  `WaitForConfirms` segue a API padrão do `RabbitMQ.Client` 6.8.1; recomendo o dev
  validar com `docker compose up postgres rabbitmq -d` antes do `/ship`.

### Pendente para outros agentes

- **test-agent**: nenhum teste novo foi escrito por mim. Os critérios de aceitação
  marcados acima foram verificados manualmente (scripts descartados) para provar que a
  implementação funciona, mas não substituem testes de verdade no `tests/`.
- **web-agent**: achado 10 (dashboard) e achado 15 (logout limpa signals). O contrato
  de `GET /api/transfers` sem `accountId` já está pronto pra uso (seção acima).

## Implementação Frontend

**Concluída em:** 2026-09-08
**Branch:** `task/MER-001-code-review-fixes`

Escopo desta entrega: achados **10** (validação) e **15** (logout limpa estado).

### Achado 10 (D7) — Transferências recentes do dashboard

`web/src/app/features/dashboard/dashboard-page.ts` já chamava
`transfersService.list({ page: 1, pageSize: RECENT_TRANSFERS_PAGE_SIZE })` **sem**
`accountId` desde o commit inicial (`git diff` contra `3fcfb62` neste arquivo está
vazio) — o bug era inteiramente do backend rejeitando a ausência do param com 400.
Não havia nenhum placeholder de `accountId`, nem tratamento que mascarasse o 400: o
`error: () => undefined` do `.subscribe()` só evita duplicar a formatação de erro (já
feita pelo `error.interceptor`, que mostra snackbar), e o template já tinha loading
state (`mat-progress-bar`) e empty state (`Nenhuma transferência ainda.`) corretos.
Com o backend agora aceitando `GET /api/transfers` sem `accountId` (200 paginado),
esse mesmo código passa a popular `recentTransfers` normalmente. **Nenhum arquivo
frontend foi alterado para este achado** — apenas validado o contrato e o fluxo
contra a seção "Implementação Backend" desta task.

### Achado 15 — Logout não limpa estado

Levantamento de todo estado em `signal()` no frontend (`grep -rn "signal(" src/app`)
mostrou que o único signal de estado **compartilhado (`providedIn: 'root'`) com dados
do usuário** é `AccountsService.accountsSignal`. `TransfersService` não guarda estado
(cada chamada de `list()`/`create()` é um HTTP direto, sem cache) — nada a resetar lá.
Os demais signals (`pending`, `isRegisterMode`, `pageIndex`, etc.) são estado de UI
local de componentes que são destruídos/recriados a cada navegação de rota (login e
dashboard estão em subárvores de rota diferentes), então não vazam entre sessões.

Solução:
- `AccountsService` ganhou `reset()`, que zera `accountsSignal` (`[]`) e `loadingSignal`
  (`false`).
- `AuthService.logout()` injeta `AccountsService` e chama `reset()` antes de navegar
  para `/login`, junto com a limpeza do token já existente.

**Correção pós-code-review (`code-reviewer-typescript`):** o `reset()` só em
`logout()` deixava passar dois caminhos de fim de sessão que não chamam `logout()`
explicitamente — `auth.guard.ts` (token expirado → redireciona pra `/login` direto)
e `error.interceptor.ts` (401 da API → só snackbar + rethrow, sem teardown). Nos
dois, `accountsSignal` continuava com as contas do usuário anterior até o próximo
`refresh()` responder, e como a navegação é client-side (sem reload), a UI podia
piscar dados do usuário anterior. Fix: mover o `reset()` para `storeToken()` (chamado
por `login()` e `register()`), que roda no início de toda sessão nova — cobre logout
explícito, expiração via guard e 401 via interceptor de uma vez, porque todos os três
caminhos terminam num login novo antes de mostrar dados de novo. Mantive o `reset()`
em `logout()` também, como defesa adicional.

### Arquivos modificados

- `web/src/app/core/accounts.service.ts` — novo método `reset()`.
- `web/src/app/core/auth.service.ts` — injeta `AccountsService`; `storeToken()` chama
  `reset()` no início de cada sessão nova (login/register); `logout()` também chama
  `accountsService.reset()`.

### Arquivos criados

- Nenhum.

### Decisões tomadas fora do escopo explícito da task

- Não escrevi spec nova para `AccountsService.reset()` nem para o comportamento de
  `AuthService.logout()` limpando contas — isso é responsabilidade do `test-agent`
  (critério de aceitação "Logout limpa contas em memória... (spec de service)" segue
  desmarcado nesse sentido; marquei como concluído do ponto de vista de
  implementação, não de cobertura de teste).

### Build/test

```
cd web && npm run build          # limpo, sem erros
cd web && npm test -- --no-watch # 3 test files, 8 passed (baseline preservada)
```

### Como testar manualmente

1. `cd server && docker compose up postgres rabbitmq -d` (ou
   `Database__Provider=Sqlite dotnet run --project src/Meridian.Api --no-launch-profile`
   com `ASPNETCORE_ENVIRONMENT=Development`), depois `cd web && npm start`.
2. Login com um usuário, criar uma conta e fazer um depósito.
3. Ir em `/dashboard` — "Transferências recentes" deve listar o depósito sem
   snackbar de erro (achado 10).
4. Fazer logout (o `accountsSignal` zera imediatamente — visível se o dashboard
   estiver montado durante o logout: os cards de conta somem antes do redirect).
5. Logar com um segundo usuário (sem contas) — o dashboard não deve mostrar, nem por
   um instante, as contas do usuário anterior (achado 15).

## Testes

**Concluídos em:** 2026-09-08 (test-agent) — nenhum arquivo de implementação foi tocado.

### Arquivos criados

Backend (`server/tests/Meridian.Tests/`):
- `Api/JwtBootstrapTests.cs` — guard `JwtOptions.EnsureConfiguredFor` (dev x não-dev,
  chave default/vazia/custom) + boot real fora de `Development` via
  `WebApplicationFactory` em `Production` (falha com mensagem citando `Jwt:Key`;
  contraprova: com chave própria o boot sobe).
- `Api/AmountScaleEndpointTests.cs` — `POST /api/transfers` e `POST /api/accounts/{id}/deposit`
  com 3 casas decimais → 422 `invalid-amount-scale` sem mover saldo; 2 casas é aceito.
- `Api/ConcurrencyRaceTests.cs` — 5 depósitos concorrentes numa moeda nova (uma única
  conta de sistema, 5 entries, cada conta creditada uma vez); 5 registros concorrentes
  (todos 201, delta do saldo da conta de sistema = -1000×N); dois registros concorrentes
  com o mesmo email (um 201, um 409 `conflict`, um único usuário no banco).
- `Api/IdempotencyConcurrencyTests.cs` — 4 requests concorrentes com a mesma key
  (só 201/409, nunca 500, depósito creditado uma vez); reserva pendente pré-inserida →
  409 `idempotency-in-flight` + `Retry-After: 1` sem executar a action; key de 250 chars
  → 422 `invalid-idempotency-key` sem executar nem gravar registro; key de 200 chars
  (limite) é aceita.
- `Api/IdempotencyFilterTests.cs` — filtro exercitado direto sobre SQLite shared-cache
  (contextos separados, então commit x rollback é observável): commit de
  `NoContentResult` (204, body vazio, escrita da action persistida), replay devolvendo o
  204 gravado sem reexecutar, rollback + liberação da reserva em `StatusCodeResult`
  não-2xx, e a **regressão do Should fix pós-review**.
- `Api/TransferListingTests.cs` — `GET /api/transfers` sem `accountId` (200 com transfers
  de todas as contas do usuário, exclui de terceiros, paginação server-side), `accountId`
  alheio ainda 404, sem token 401.
- `Api/HealthEndpointTests.cs` — `/health` 200 `connected` com DB ok; 503 `degraded`/
  `unavailable` com Postgres inacessível (`Port=1`).
- `Infrastructure/OutboxProcessorTests.cs` — publish falhando deixa a mensagem pendente e
  incrementa `Attempts`; publish com sucesso marca `ProcessedAt` (mock só no
  `IEventPublisher`, que é a borda real do RabbitMQ).
- `Infrastructure/RabbitMqEventPublisherTests.cs` — `TryPublish` com broker fora do ar
  retorna `false`.

Frontend (`web/src/app/`):
- `core/accounts.service.spec.ts` — signal populado por `refresh()`, `reset()` zera
  `accounts`/`loading`, `deposit()` refaz o refresh e manda `Idempotency-Key` **distinta
  a cada chamada** (formato UUID v4 + `not.toBe` entre dois depósitos).
- `core/transfers.service.spec.ts` — `list()` sem `accountId` não envia o param (envia
  `page`/`pageSize`), `list({accountId})` envia, `create()` manda `Idempotency-Key` no
  formato UUID v4 e **distinta a cada chamada**.
- `features/dashboard/dashboard-page.spec.ts` — dashboard pede `GET /api/transfers` sem
  `accountId` e renderiza a linha devolvida pela listagem não filtrada.

### Arquivos modificados (só acréscimo, nenhum teste existente enfraquecido)

- `server/tests/Meridian.Tests/Domain/TransferTests.cs` — guard de escala em
  `Transfer.Execute` (rejeita 10.005 / 0.001 / 99.999 sem mexer em saldo; aceita 10.99).
- `web/src/app/core/auth.service.spec.ts` — `logout()` limpa as contas em memória;
  `login()` e `register()` limpam o estado da sessão anterior (o `reset()` mora em
  `storeToken()`).

### Cobertura por critério de aceitação

| Critério / decisão | Teste que prova |
|---|---|
| Boot fora de Development sem `Jwt:Key` falha (D4) | `JwtBootstrapTests.Boot_OutsideDevelopment_WithoutAConfiguredKey_Fails` (+ `Boot_..._WithAConfiguredKey_Succeeds`) |
| `amount: 10.005` → 422 `invalid-amount-scale` (D1) | `AmountScaleEndpointTests.Transfer_WithMoreThanTwoDecimals_Returns422_AndMovesNoMoney`, `.Deposit_WithMoreThanTwoDecimals_...`, `TransferTests.Execute_AmountWithMoreThanTwoDecimals_Throws_AndLeavesBalancesUntouched` |
| N concorrentes numa moeda nova → uma conta de sistema (D2) | `ConcurrencyRaceTests.ConcurrentDeposits_InABrandNewCurrency_CreateExactlyOneSystemAccount` |
| Registro concorrente não retorna 409 — retry funciona (achado 4) | `ConcurrencyRaceTests.ConcurrentRegistrations_AllSucceed_DespiteRacingOnTheSystemAccountBalance` |
| Mesma `Idempotency-Key` concorrente: nunca 500, nunca dupla execução (D3/achado 5) | `IdempotencyConcurrencyTests.ConcurrentRequests_WithTheSameKey_DepositOnce_AndNeverFailWith500` + `.Request_WithAKeyStillInFlight_Returns409_WithRetryAfter_AndDoesNotExecute` |
| Key com 250 chars → 422 antes da action (achado 12) | `IdempotencyConcurrencyTests.OversizedKey_Returns422_BeforeTheActionRuns` (+ limite exato em `.KeyAtTheMaximumLength_IsAccepted`) |
| Dois `register` concorrentes do mesmo email → 201 + 409 (achado 11) | `ConcurrencyRaceTests.ConcurrentRegistrations_WithTheSameEmail_ProduceOneCreatedAndOneConflict` |
| `TryPublish` com broker fora → false; outbox pendente, `Attempts` incrementa (D6/achado 6) | `RabbitMqEventPublisherTests.TryPublish_WithAnUnreachableBroker_ReturnsFalse` + `OutboxProcessorTests.UnpublishedMessage_StaysPending_AndRegistersAnAttempt` |
| `GET /api/transfers` sem `accountId` → 200 paginado (D7) | `TransferListingTests.List_WithoutAccountId_Returns200_WithTransfersFromEveryAccountOfTheUser`, `.ExcludesTransfersOfOtherUsers`, `.IsPaginated` |
| Dashboard carrega "Transferências recentes" (achado 10) | `dashboard-page.spec.ts` — "asks for the transfers of every account, without an accountId filter" e "renders the recent transfers returned by the unfiltered listing" |
| Logout limpa contas; sessão nova não mostra dados do anterior (achado 15) | `auth.service.spec.ts` — "clears the in-memory accounts on logout", "…when another user logs in", "…when another user registers"; `accounts.service.spec.ts` — "clears the accounts signal on reset" |
| Toda escrita financeira envia `Idempotency-Key` única por ação (regra do CLAUDE.md) | `accounts.service.spec.ts` — "sends a different Idempotency-Key on each deposit"; `transfers.service.spec.ts` — "posts the transfer with a random Idempotency-Key" e "sends a different Idempotency-Key on each create" |
| `/health` 503 com DB fora, 200 com DB ok (D8) | `HealthEndpointTests.Health_WithUnreachableDatabase_Returns503_AndReportsUnavailable`, `.Health_WithReachableDatabase_Returns200_AndReportsConnected` |
| Commit do filtro cobre `NoContent()`/`StatusCodeResult` 2xx (D9) | `IdempotencyFilterTests.Commit_CoversA2xxStatusCodeResult_WithoutABody`, `.Replay_OfACommittedNoContentResponse_ReturnsTheStoredStatus`, `.NonSuccessStatusCodeResult_RollsBackTheAction_AndReleasesTheReservation` |
| Reserva não fica presa quando a action esgota o `ConcurrencyRetry` (Should fix pós-review) | `IdempotencyFilterTests.FailedAction_WithStaleTrackedEntities_ReleasesTheReservation_AndKeepsTheOriginalException` e `.FailedAction_WithStaleTrackedEntities_LeavesTheKeyUsableForARetry` |

O cenário da regressão foi validado como não-tautológico: um teste descartável
reproduziu o `Remove` + `SaveChanges` **sem** `ClearTracking()` no mesmo estado (entidade
com `Version` stale rastreada) e ele lança `ConcurrencyConflictException` deixando o
registro no banco — ou seja, os dois testes acima falhariam contra o código pré-fix.

### Fora de cobertura automatizada (deliberado)

- `docker compose up --build` e `.dockerignore` (achado 14), creds do broker (D5) e a
  publicação real no exchange `meridian.events`: exigem Docker/RabbitMQ, continuam como
  validação manual do dev antes do `/ship`.
- Semântica fina dos publisher confirms (ack negativo / timeout de `WaitForConfirms`):
  sem broker (nem fake de protocolo AMQP) só o caminho "broker fora" é observável.
- D10 (conexão SQLite por scope): sem teste dedicado — é exercitado indiretamente pelos
  testes de corrida, que só passam porque cada scope abre a própria conexão.
- Migrations: sem teste dedicado, por regra do CLAUDE.md.

### Resultado

```
cd server && dotnet build            # 0 Warning(s), 0 Error(s)
cd server && dotnet test             # 43 → 81 passed, 0 failed (3 execuções seguidas, sem flake)
cd web    && npm run build           # limpo
cd web    && npm test -- --no-watch  # 8 → 21 passed, 0 failed (6 test files)
```

- Baseline antes: 43 passed / 0 failed (backend), 8 passed / 0 failed (frontend).
- Depois: 81 passed / 0 failed (backend, +38), 21 passed / 0 failed (frontend, +13).
- Nenhum bug novo encontrado na implementação.

### Ajustes após o code review dos testes

- **S1 (Should fix):** a asserção de `Idempotency-Key` era só `toBeTruthy()` — sobrevivia
  a trocar `crypto.randomUUID()` por uma constante, que é o pior bug possível nesse
  header (segundo depósito viraria replay do primeiro no backend). Agora cada spec faz
  duas chamadas seguidas e afirma formato UUID v4 + `expect(first).not.toBe(second)`;
  uma key constante mata as duas asserções. `accounts.service.spec.ts` e
  `transfers.service.spec.ts` (+2 testes).
- **N1 (Nit):** `auth.service.spec.ts` reformatado com `npx prettier --write`;
  `npx prettier --check` nos specs passa limpo.
