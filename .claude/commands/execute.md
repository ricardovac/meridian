---
description: Executa uma task do meridian. Aceita MER-XXX ou caminho do arquivo. Delega ao api-agent e/ou web-agent, roda code review, depois test-agent.
argument-hint: [MER-XXX | caminho do arquivo]
---

Execute a implementação da task: **$ARGUMENTS**

Fluxo:

1. **Resolver o arquivo da task**:
   - Se `$ARGUMENTS` já for um caminho `.md`, usar direto
   - Se começar com `MER-`, resolver via glob em `.claude/tasks/MER-XXX*.md` — sem
     fallback; se não achar, reportar e parar
   - Outro prefixo: recusar explicando que este repo usa `MER-XXX`
   - Se achar múltiplos, perguntar qual usar

2. **Verificar issues relacionadas** — ler o frontmatter da task:
   - Se já existir `issues: [...]` com valores, registrar para uso no `/ship`
   - Se não existir e o repo tiver remote no GitHub (`gh repo view` funciona),
     perguntar: *"Existe uma issue relacionada? Informe o número ou enter para pular."*
     Se informado, adicionar `issues: [<número>]` ao frontmatter

3. **Definir a branch base — SEMPRE perguntar ao dev** antes de delegar:

   Primeiro `git fetch origin` se houver remote (base local pode estar desatualizada —
   pular isso já causou reimplementação de trabalho existente em outro projeto). Então:

   > *"A partir de qual branch executo esta task?"*
   > 1. **`main`** — feature independente
   > 2. **branch atual** (`<nome>`) — empilhar nesta stack

   - **main**: criar `task/MER-XXX-<slug>` a partir de `origin/main` atualizado (ou
     `main` local se não há remote).
   - **branch atual**: a task **empilha** na corrente. Antes de criar a branch nova,
     commitar trabalhos pendentes pré-existentes (commits atômicos, Conventional
     Commits) — sem isso as mudanças pendentes vazam pro diff da task. Criar
     `task/MER-XXX-<slug>` a partir da branch atual.

   Instruir os agentes explicitamente a **trabalhar nessa branch e NÃO criar/trocar de
   branch**. Worktree só pra task 100% independente a partir de main.

4. **Identificar o escopo da task** (seções da task dizem): backend (`server/`),
   frontend (`web/`) ou ambos.
   - Backend → delegar ao **`api-agent`** — ele lê task + CLAUDE.md e implementa. Não
     escreve testes; a entrega é a implementação com a suíte existente verde.
   - Frontend → delegar ao **`web-agent`** — mesma lógica. Se a task tem os dois
     lados, backend primeiro (o contrato final que o api-agent registra na task é o
     input do web-agent).

5. Após implementação, **rodar code review**:
   - Parte backend → agente `code-reviewer` (usa as regras do CLAUDE.md deste repo,
     incluindo severidades Blocker/Should fix/Nit)
   - Parte frontend → agente `code-reviewer-typescript`

6. Se o review apontar violações, voltar pro agente implementador corrigir — loop até
   aprovar (Blockers e Should fix resolvidos).

7. **Aprovada a implementação, delegar ao `test-agent`** — escreve os testes (xUnit
   e/ou Vitest). Regras do fluxo:
   - Migrations não recebem teste dedicado — comportamento, não schema.
   - O `test-agent` **nunca edita código de implementação** — só testes. Se um teste
     dele revelar bug, ele para e reporta; o ciclo volta pro agente implementador
     corrigir, e depois retorna pro `test-agent` continuar.
   - Ele não pode enfraquecer, deletar ou pular teste existente.

8. **Mandar os testes pro code review** — asserção real (não tautológica), mock só na
   borda, sem dependência de ordem. Problema → volta pro `test-agent` — loop até
   aprovar.

9. Quando aprovado, reportar resumo final com:
   - Arquivos criados/modificados (implementação e testes, separados)
   - Resultado de `dotnet test` e `npm test`/`npm run build`, com **baseline antes e
     depois** — se falhas subiram, qual teste e por quê
   - Confirmação de que as seções "Implementação Backend/Frontend" (e "Testes") foram
     preenchidas na task
   - Qualquer bug que os testes revelaram e como foi resolvido
   - Próximo passo sugerido: testar manualmente e rodar `/ship MER-XXX`

**Importante:** NÃO commitar a implementação nem os testes da task — isso é do
`/ship`, só após o dev testar e aprovar. Exceção (passo 3, opção "branch atual"):
commitar trabalhos pendentes **pré-existentes** antes de empilhar é permitido e
necessário — mas nunca o código novo desta task.
