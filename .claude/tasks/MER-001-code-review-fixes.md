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

- [ ] Boot fora de Development com `Jwt:Key` ausente/default falha com erro claro
- [ ] `POST /api/transfers` com `amount: 10.005` → 422 `invalid-amount-scale`
- [ ] N registros/depósitos concorrentes numa moeda nova criam **uma** conta de sistema (teste de corrida)
- [ ] Registro concorrente com movimentação na conta de sistema não retorna 409 (retry funciona)
- [ ] Duas requests concorrentes com a mesma `Idempotency-Key`: uma executa, a outra recebe replay ou 409 `idempotency-in-flight` — nunca 500, nunca dupla execução
- [ ] `Idempotency-Key` com 250 chars → 422 antes de executar a action
- [ ] Dois `register` concorrentes do mesmo email → um 201, um 409
- [ ] `TryPublish` com broker fora retorna false e a linha do outbox permanece não processada (Attempts incrementa)
- [ ] `GET /api/transfers` sem `accountId` → 200 paginado com transfers de todas as contas do usuário
- [ ] Dashboard renderiza "Transferências recentes" sem snackbar de erro
- [ ] Logout limpa contas em memória; login de outro usuário não exibe dados do anterior (spec de service)
- [ ] `/health` → 503 com Postgres fora; 200 com DB ok
- [ ] `docker compose up --build` funciona após `dotnet build` local (com `.dockerignore`)
- [ ] `dotnet run` + `docker compose up postgres rabbitmq -d` publica evento no exchange `meridian.events` (validável na management UI)
- [ ] Suítes backend e frontend verdes; baseline não regride

## Não faz parte do escopo

- Cauda de limpeza do review (bloco de persistência triplicado, contracts de response
  ausentes, `TransferStatus.Failed` morto, guard de ownership duplicado, índices
  compostos em `Transfers`, memoização do `isAuthenticated`, CI sem `npm test`) —
  vira MER-002.
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
