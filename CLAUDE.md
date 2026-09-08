# CLAUDE.md — meridian

## Visão Geral

Plataforma de pagamentos com **ledger de dupla entrada**, projeto de portfólio. Monorepo:

```
server/   → API em C# / .NET 8 (ASP.NET Core, EF Core, PostgreSQL, RabbitMQ)
web/      → SPA em Angular 21 (standalone, signals, zoneless, Angular Material)
```

Padrões centrais do domínio (não são detalhes de implementação — são o produto):
**dupla entrada contábil**, **idempotência por header**, **transactional outbox**,
**concorrência otimista com retry**. Toda mudança precisa preservá-los.

---

## Comandos

```bash
# Backend (de server/)
dotnet build
dotnet test                                                    # SQLite in-memory, sem infra
dotnet run --project src/Meridian.Api                          # Postgres (docker compose up postgres rabbitmq -d)
Database__Provider=Sqlite dotnet run --project src/Meridian.Api --no-launch-profile   # sem docker

# Frontend (de web/)
npm start                # ng serve com proxy /api → http://localhost:5080
npm test -- --no-watch   # Vitest headless
npm run build

# Tudo
docker compose up --build   # web :4200, api :5080 (interno 8080), rabbitmq UI :15672
```

API dev roda em `http://localhost:5080` (launchSettings). Container interno: `8080`.

### ⚠️ Comandos destrutivos no DB — bloqueados

`dotnet ef database drop`, `docker compose down -v`, `docker volume rm` de volumes do
projeto, e `DROP/TRUNCATE` via psql **destroem o DB de dev** (volume `pgdata`). O hook
`.claude/hooks/guard-destructive-db.sh` bloqueia esses comandos — é proposital. Não há
motivo legítimo pra um agente derrubar o Postgres: **os testes usam SQLite in-memory**
e nunca tocam o DB de dev. Se schema local divergir, a saída é migration incremental
nova (`dotnet ef migrations add`), nunca drop. Precisando dropar de verdade, o dev roda
manualmente fora do Claude.

---

## Arquitetura — server/

Clean Architecture. Direção de dependência: `Api → Infrastructure → Application → Domain`.

```
src/Meridian.Domain/           → Entities/ (Account, Transfer, LedgerEntry, User,
                                 OutboxMessage, IdempotencyRecord), Events/, Exceptions/
                                 ZERO dependências. Invariantes vivem aqui.
src/Meridian.Application/      → Abstractions/ (IClock, IUnitOfWork, interfaces de repo,
                                 IPasswordHasher, ITokenService), Common/ (ConcurrencyRetry,
                                 PagedResult), Dtos/, Exceptions/, Services/ (AuthService,
                                 AccountService, TransferService, SystemAccountProvider,
                                 OutboxWriter)
src/Meridian.Infrastructure/   → Persistence/ (DbContext, Repositories/, Migrations/),
                                 Messaging/ (RabbitMqEventPublisher, OutboxProcessor),
                                 Security/, Time/
src/Meridian.Api/              → Controllers/, Contracts/ (request/response models),
                                 Filters/ (IdempotencyFilter), Middleware/
                                 (ExceptionHandlingMiddleware), Security/
tests/Meridian.Tests/          → Domain/, Application/, Infrastructure/, Api/
```

### Regras do domínio (invioláveis)

- **Dinheiro nunca aparece nem some.** Toda movimentação é um `Transfer` que grava
  **exatamente 2 `LedgerEntry`** (débito + crédito) na **mesma transação**. `Balance` é
  projeção cacheada — **proibido** alterar `Balance` sem as entries correspondentes.
  Depósito não é exceção: é transferência a partir da conta de sistema da moeda.
- **`LedgerEntry` é imutável e append-only.** Nunca UPDATE/DELETE em entries. Correção
  de erro = transferência de estorno (entries novas), nunca edição do histórico.
- **Invariantes na entity, não no service.** `Transfer.Execute(...)` (factory de domínio)
  é quem valida: amount > 0, mesma moeda, fundos suficientes (conta de sistema pode
  negativar), sem self-transfer. Service orquestra; quem decide é o domínio. Regra de
  negócio nova entra como guard na factory/método de domínio — **Blocker** se aparecer
  como `if` solto num service ou controller.
- **Dinheiro é `decimal`.** `double`/`float` em valor monetário é **Blocker**.
- **IDs são `Guid`.** Entidade nova nasce com PK `Guid` — int auto-increment é **Blocker**
  sem justificativa documentada na task.
- **Estado inicial não vem do payload HTTP.** Status, defaults e sequências são decididos
  pela entity/factory. Caller passa contexto, não o resultado. — **Should fix**.

### Regras de aplicação

- **Escrita com efeito financeiro exige `Idempotency-Key`** (atributo `[Idempotent]` +
  `IdempotencyFilter`). O `IdempotencyRecord` é gravado na **mesma transação** do efeito.
  Replay devolve a resposta original; mesma key com payload diferente → 422. Endpoint
  novo de escrita financeira sem `[Idempotent]` é **Blocker**.
- **Evento de domínio → `OutboxMessage` na mesma transação** (via `OutboxWriter`). Quem
  publica no RabbitMQ é o `OutboxProcessor` (background), com tolerância a broker fora
  do ar. Publicar direto no broker dentro do request é **Blocker** — perde o evento se o
  commit falhar (e vice-versa).
- **Mutação de saldo roda sob `ConcurrencyRetry`** (token `Version` + retry até 3x em
  `ConcurrencyConflictException`). Remover o retry ou o token de uma operação de saldo
  é **Blocker**.
- **Services são orquestradores legíveis.** Método público curto, sequência de chamadas
  a privados com nome descritivo; estado intermediário em variável local ou privado que
  retorna o agregado carregado (`var account = await Validate(...)`) — não re-fetch.
  Método público que concentra validação + mutação + montagem inline é **Should fix**.
- **Privados com ≤3 parâmetros.** Com 4+, repensar: agrupar no DTO de entrada existente,
  passar a entity, ou dividir responsabilidade — **Should fix**. Privado que precisa de
  >1 atributo do mesmo DTO de entrada recebe o DTO inteiro — **Nit**.
- **Dois privados com >70% do corpo idêntico** (variação create/update, deposit/transfer)
  → unificar com branching mínimo no que diverge — **Should fix**.
- **Repos retornam entidades de domínio** (ou `PagedResult<T>`/projeções documentadas).
  Service não monta SQL nem `DbContext` direto — DbContext é detalhe da Infrastructure.

### Regras da borda (Api)

- **Controllers são finos**: bind do request → chama **um** service → mapeia pro
  contract/response. Proibido na borda: decisão de negócio, orquestração de múltiplos
  repos, transação, manipulação de entity. Controller com regra de negócio é **Blocker**.
- **Erro sai só como RFC 7807 ProblemDetails**, via `ExceptionHandlingMiddleware`.
  Exceção de domínio nova → mapear no middleware (`insufficient-funds` e
  `currency-mismatch` → 422, not found → 404, conflito → 409). `try/catch` formatando
  erro no controller é **Should fix**.
- **Ownership**: recursos são filtrados pelo usuário do token. Conta/transfer de outro
  usuário responde **404** (não 403 — não vazar existência). Endpoint novo que esquece o
  filtro de ownership é **Blocker**.
- **Contracts da API em `Api/Contracts/`** — request/response HTTP não são DTOs da
  Application nem entities. JSON camelCase, enums como string (`JsonStringEnumConverter`).

### Migrations

- **Migration aplicada é imutável.** Mudança de schema = migration incremental nova
  (`dotnet ef migrations add <Nome>` — via tool manifest local, `dotnet tool restore`).
  Editar migration existente em `Infrastructure/Migrations/` é **Blocker**: `Migrate()`
  só roda pendentes; a edição quebra silenciosamente qualquer DB onde ela já rodou.
- SQLite (testes) usa `EnsureCreated` — migration nova precisa manter o model compatível
  com os dois providers (nada de SQL raw Postgres-only no model; se inevitável, isolar
  por provider).

### Estilo C#

- `nullable enable` em tudo; build com **zero warnings**.
- **Zero comentário descritivo** em código novo. Só WHY genuinamente não-óbvio e XML doc
  funcional. Prosa explicando o que a linha faz é **Should fix**.
- Injeção via construtor (primary constructor ok); nunca service locator.
- Records/`readonly` pra DTOs e contracts quando aplicável; enums nativos.

---

## Arquitetura — web/

```
src/app/core/      → models.ts (contratos da API), auth.service, accounts.service,
                     transfers.service (estado em signals), auth.interceptor,
                     error.interceptor, auth.guard, paginator-intl
src/app/           → login/, shell/ (toolbar+sidenav), dashboard/, transfer/,
                     account-detail/ — todas rotas lazy via loadComponent
```

### Regras

- **Standalone + signals + OnPush + `inject()`** em tudo. NgModule novo, `any`,
  constructor injection e componente sem OnPush são **Should fix**.
- **Estado em signals nos services** (`accounts.service` expõe signal e o refresh após
  mutação). Não introduzir NgRx/store externo sem task explícita.
- **Forms tipados** (typed reactive forms) com validação e `mat-error` por campo.
- **HTTP só via services do `core/`** com tipos de `models.ts` — componente não chama
  `HttpClient` direto — **Should fix**.
- **Toda escrita financeira envia `Idempotency-Key: crypto.randomUUID()`**, gerado por
  ação do usuário (o service já faz isso — manter o padrão em endpoint novo).
- **Erros**: `error.interceptor` mapeia ProblemDetails → snackbar; `insufficient-funds`
  é exceção — renderiza inline no form. Componente não formata erro HTTP por conta
  própria.
- **Listas grandes = paginação server-side** (`Paged<T>` + MatPaginator), nunca carregar
  tudo e paginar no client.
- Labels de UI em **português**; código/identificadores em inglês. Valores com currency
  pipe (locale pt).

---

## Testes

Escritos pelo `test-agent` (nunca por quem implementou — ver `.claude/agents/`).

- **Backend**: xUnit em `tests/Meridian.Tests/`, espelhando a estrutura de `src/`.
  Integração via `MeridianApiFactory` (WebApplicationFactory + SQLite `:memory:` com
  conexão mantida aberta). Sem mock de repo/DbContext — o SQLite é o banco de teste;
  mock só na borda real (RabbitMQ, clock).
- **Frontend**: Vitest + jsdom (headless). Specs de service (estado/token) e smoke de
  componente.
- Asserção tem que provar comportamento (se a implementação estivesse errada, o teste
  falharia?). Um conceito por teste, nome descritivo, sem dependência de ordem.
- Migrations não recebem teste dedicado — o comportamento que a constraint garante é
  testado pelo domínio.

---

## Versionamento e fluxo

- SemVer `vMAJOR.MINOR.PATCH`, tags limpas (sem sufixo). Release via `/ship` (delega ao
  `git-agent`): Conventional Commits, PR contra `main`, tag + release no GitHub.
- Tasks locais: `.claude/tasks/MER-XXX-<slug>.md` (criadas por `/request-task`,
  executadas por `/execute`). Branch: `task/MER-XXX-<slug>`.
- Severidades usadas pelos code-reviewers neste repo: **Blocker** (não mergeia),
  **Should fix** (corrigir antes do ship), **Nit** (a critério).
