---
description: Abre uma issue de feature/enhancement/refactor/question no repo do meridian no GitHub. Para bugs, use /report-bug. Para task local executável, use /request-task.
argument-hint: [descrição da funcionalidade ou melhoria]
---

O usuário quer abrir uma issue (feature, enhancement, refactor ou question). A
descrição dele é:

**$ARGUMENTS**

Fluxo:

1. **Pré-requisito**: verificar que o repo tem remote no GitHub (`gh repo view`). Se
   não tiver, avisar que o repo ainda não foi publicado e parar (sugerir criar com
   `gh repo create`).

2. **Coletar informações** — campos obrigatórios:
   - Tipo (feature / enhancement / refactor / question)
   - Descrição (o que deve ser feito e por quê)
   - Critérios de aceitação (checklist)
   - Prioridade (high / medium / low)

   Opcionais: contexto (problema que resolve), task relacionada (`MER-XXX`), links.

   Se `$ARGUMENTS` já cobrir os obrigatórios, inferir o restante. Se faltar, fazer
   **uma única rodada** de perguntas curtas.

3. **Inferir o tipo**:
   - `feature` → algo novo que não existe
   - `enhancement` → melhoria em algo existente
   - `refactor` → reestruturação sem mudança de comportamento
   - `question` → dúvida ou investigação

4. **Inferir task relacionada** (opcional): se o usuário mencionou `MER-XXX`, citar no
   corpo; senão, se a branch atual é `task/MER-XXX-*`, perguntar se há relação.

5. **Montar o corpo** em markdown limpo, seções nesta ordem: Tipo, Contexto,
   Descrição, Critérios de aceitação (checklist), Task relacionada, Prioridade.
   Preservar o que o usuário disse — não parafrasear em excesso. Se faltarem
   critérios, inferir 2 a 4 mensuráveis.

6. **Abrir a issue** com `gh issue create`:
   - Título: resumo direto (até 72 chars), sem prefixo de tipo
   - Labels: `enhancement` pra feature/enhancement; `refactor`; `question` (criar o
     label se não existir)
   - Sem assignee por default

7. **Reportar**: link da issue + resumo do preenchido.

Se o usuário responder com informações novas depois, **não abrir outra issue** —
comentar ou editar a existente.
