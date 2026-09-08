---
description: Ship de uma task aprovada — commits atômicos + push + PR contra main + tag/release SemVer. Aceita MER-XXX ou caminho do arquivo. Delega ao git-agent.
argument-hint: [MER-XXX | caminho do arquivo]
---

A task **$ARGUMENTS** foi aprovada por mim após testes locais. Prossiga com o ship.

Fluxo:

1. **Resolver o arquivo da task**:
   - Caminho `.md` → usar direto
   - `MER-XXX` → glob **apenas** em `.claude/tasks/MER-XXX*.md` — sem fallback
   - Outro prefixo → recusar explicando que este repo usa `MER-XXX`

   Registrar `<task_id>` e o slug. Tudo abaixo usa esses valores.

2. **Pré-requisito de remote**: se o repo ainda não tem remote no GitHub
   (`gh repo view` falha), perguntar se cria agora (`gh repo create ricardovac/meridian
   --public --source=. --push` após o primeiro commit) ou se o ship fica só local
   (commits + tag, sem PR/release).

3. **Delegar ao agente `git-agent`** com estas instruções:
   a. Verificar estado do git
   b. Garantir que a branch atual se chama `task/<task_id>-<slug>` — se não, criar a
      partir do HEAD atual e fazer checkout antes de prosseguir
   c. Planejar commits atômicos seguindo Conventional Commits (separar implementação,
      testes e docs em commits distintos quando fizer sentido)
   d. Apresentar o plano pra eu confirmar
   e. Após confirmação, executar commits
   f. Push da branch
   g. Abrir PR contra `main`, linkando no corpo o arquivo da task em `.claude/tasks/`
      (e `Closes #N` pra cada issue do frontmatter da task)

4. **Criar tag + release** apontando pro HEAD da branch pushada. **SemVer 2.0.0, tag
   sempre limpa `vMAJOR.MINOR.PATCH`** — nunca sufixo (`-beta.N`, `-rc.N`):
   - `git fetch --tags`; última tag via `git tag --sort=-version:refname | head -1`
     (repo sem tag → começar em `v0.1.0`)
   - Perguntar o bump: **patch** (default — fix/refactor/feature pequena), **minor**
     (feature significativa) ou **major** (breaking na API pública)
   - `gh release create vX.Y.Z --target task/<task_id>-<slug> --generate-notes`, com
     title = nome da tag e body incluindo contexto + critérios de aceitação da task +
     link do PR

5. **Fechar issues relacionadas** — campo `issues` do frontmatter:
   - Com valores → pra cada número: `gh issue close <n> --comment "Fechado pelo
     release <tag>. Veja: <link_do_release>"` (se o PR já não fechar via `Closes #N`)
   - Vazio/ausente → pular silenciosamente

6. **Atualizar o status da task** no frontmatter: `status: 🟢 Shipped`.

7. **Reportar** em tabela markdown com links clicáveis (URLs cruas, sem encurtar):

   ```markdown
   ## Ship `<task_id>` — concluído

   ### Pull Request

   | # | Título | Link |
   |---|---|---|
   | #<num> | <título> | https://github.com/ricardovac/meridian/pull/<num> |

   ### Release

   | Tag | Link |
   |---|---|
   | <tag> | https://github.com/ricardovac/meridian/releases/tag/<tag> |

   ### Issues fechadas

   <se houver; senão omitir a seção inteira>

   ### Branch

   | Nome | Base | HEAD |
   |---|---|---|
   | `task/<task_id>-<slug>` | main | `<sha curto>` |
   ```

   Após as tabelas, parágrafo final: próximo passo é revisar e mergear o PR; a tag já
   aponta pro HEAD da branch — se `main` receber outros merges antes deste, avaliar
   se vale recriar a release após o merge pra capturar tudo.

**Importante:** commits só acontecem aqui, com meu OK no plano do `git-agent` — nunca
durante o `/execute`.
