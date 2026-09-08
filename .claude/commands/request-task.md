---
description: Cria um arquivo de task em `.claude/tasks/MER-XXX-*.md` seguindo o padrão do repo. Se o repo tiver remote no GitHub, abre a issue e linka. Para apenas a issue (sem task), use /request-feature.
argument-hint: [descrição da task — escopo, motivação, contexto]
---

O usuário quer criar uma **task local** (`.claude/tasks/MER-XXX-*.md`). A descrição:

**$ARGUMENTS**

Tasks locais (`MER-XXX`) são a spec de implementação que o `/execute` consome:
detalhes de arquitetura, schema, contrato HTTP, critérios de aceitação granulares e
as seções "Implementação Backend/Frontend" que os agentes preenchem depois.

## Fluxo

### 1. Inferir próximo `MER-XXX`

```bash
ls .claude/tasks/ | grep -oE '^MER-[0-9]+' | sort -V | tail -1
```

Maior número + 1, padding de 3 dígitos (`MER-001`, `MER-042`).

### 2. Coletar informações essenciais

Se `$ARGUMENTS` cobrir o suficiente, inferir o restante. Se faltar algo crítico,
fazer **uma única rodada** de perguntas curtas:

- **Título humano** (1 linha, sem prefixo `MER-XXX`)
- **Severity** (`low` / `medium` / `high`)
- **Escopo**: backend (`server/`), frontend (`web/`) ou ambos
- **`blocked_by`** se depende de outra task ainda não mergeada
- **Tipo** (feature nova vs enhancement vs refactor)

### 3. Inferir relacionamentos

- Task mencionada em `$ARGUMENTS` → `related: [...]`
- Branch atual é `task/MER-XXX-*` → perguntar se a nova task **deriva** desta
  (`blocked_by`)
- Tasks recentes em `.claude/tasks/` tocando o mesmo módulo → sugerir como `related`

### 4. Gerar o arquivo

Caminho: `.claude/tasks/MER-XXX-<slug-do-titulo>.md` (slug: lowercase, hífens, sem
acentos).

Frontmatter obrigatório:

```yaml
---
id: MER-XXX
title: <título humano>
status: 🟡 Planejada
repo: meridian
scope: server | web | fullstack
created_at: <YYYY-MM-DD de hoje>
related: []
issues: []
blocked_by: []
blocks: []
severity: low | medium | high
---
```

Seções (omitir as que não fizerem sentido):

1. `## Contexto` — por que essa task existe
2. `## Estado atual` — o que já existe no código relacionado
3. `## Decisões já fechadas` (opcional) — tabela de decisões prévias com o usuário
4. **Seções específicas** (numeradas: `## 1. ...`) — schema, contrato HTTP, algoritmo,
   estrutura de arquivos
5. `## Critérios de aceitação` — checklist `- [ ]` mensurável
6. `## Não faz parte do escopo` — explícito o que **não** entra
7. `## Branch e merge` — `task/MER-XXX-<slug>`, base (main ou outra task)
8. `## Estimativa` — sessões previstas, contagem aproximada de arquivos
9. `## Riscos monitorados` — tabela `Risco | Mitigação` (3-4 riscos reais, não
   genéricos)

> **Não** adicionar seções `## Implementação Backend/Frontend` — os agentes preenchem
> no `/execute`.

### 5. Convenções a seguir

- **Branch base**: `blocked_by` com PR aberta → deriva da branch dessa task; senão
  `main`.
- **Critérios granulares**: cada bullet **testável** ("endpoint X retorna 201 com
  shape Y", não "implementar bem").
- Endpoint novo → documentar: path completo, validações, shape do 200/201 (JSON
  literal), erros 4xx com `type` do ProblemDetails, auth. **Escrita financeira →
  anotar exigência de `Idempotency-Key`.**
- Schema novo → documentar migration: colunas, tipos, índices, FKs. Lembrar: `Guid`
  PK, `decimal` pra dinheiro.
- Frontend → telas/rotas afetadas, estados (loading/vazio/erro), labels pt-BR.

### 6. Confirmar com o usuário antes de criar

Mostrar: caminho final, frontmatter completo e títulos das seções planejadas.
Aprovação → escrever via `Write`.

### 7. Abrir issue no GitHub — se houver remote

Se `gh repo view` funcionar neste repo: criar a issue (fluxo do `/request-feature`,
usando o conteúdo da task), adicionar no body `Detalhes em
\`.claude/tasks/MER-XXX-<slug>.md\``, e atualizar o frontmatter com `issues: [<n>]`.

Se o repo ainda não tem remote no GitHub, pular com aviso — o `/ship` lembra de criar
o repo primeiro.

**Exceção**: se o dev já passou um número de issue no `$ARGUMENTS`, reutilizar.

### 8. Reportar

- Caminho do arquivo criado, resumo do frontmatter
- Link da issue (se criada)
- Próximo passo: `/execute MER-XXX`

## Restrições

- **NÃO** preencher seções de implementação — só os agentes fazem isso.
- **NÃO** marcar critérios como concluídos — todos começam vazios.
- **NÃO** criar branches nem alterar working tree — task é só documento.
- `$ARGUMENTS` vago demais → pedir esclarecimento antes; a qualidade da task vem da
  especificidade.

## Exemplos de invocação

- `/request-task Webhooks com assinatura HMAC consumindo meridian.events: registro de endpoint por usuário, delivery com retry exponencial. Severity medium, escopo fullstack.`
- `/request-task Extrato da conta em CSV: GET /api/accounts/{id}/entries/export. Backend only, deriva de nada, severity low.`
