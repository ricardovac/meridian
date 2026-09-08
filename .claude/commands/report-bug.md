---
description: Abre uma issue de bug no repo do meridian no GitHub a partir do relato do tester.
argument-hint: [descrição livre do bug]
---

O tester encontrou um bug e quer abrir uma issue. A descrição dele é:

**$ARGUMENTS**

Fluxo:

1. **Pré-requisito**: verificar remote no GitHub (`gh repo view`). Sem remote →
   avisar e parar.

2. **Coletar informações** — campos obrigatórios:
   - O que aconteceu
   - O que deveria acontecer
   - Passos para reproduzir (numerados)
   - Severidade (blocker / high / medium / low)
   - Camada afetada (api / web / infra / não sei)
   - Ambiente (dev local via `dotnet run`/`ng serve`, docker compose, ou outro;
     browser se for no web)

   Se `$ARGUMENTS` já cobrir tudo, seguir. Se faltar campo obrigatório, fazer **uma
   única rodada** de perguntas curtas.

   Pra bugs de API, pedir se possível: request (método, path, body), response
   (status + ProblemDetails) e, se envolver dinheiro, os saldos/entries esperados vs
   observados.

3. **Inferir task relacionada** (opcional): `MER-XXX` mencionada → citar; senão, se a
   branch atual é `task/MER-XXX-*`, perguntar se há relação.

4. **Montar o corpo** com as seções na ordem do passo 2, markdown limpo, preservando
   o que o tester disse.

5. **Abrir a issue** com `gh issue create`:
   - Título: `[bug]` + resumo curto do sintoma
   - Label: `bug`; se houver label de severidade, aplicar também
   - Sem assignee por default

6. **Reportar**: link da issue + resumo. Lembrar que dá pra anexar
   screenshot/log comentando depois.

Se o tester responder com informações novas ("esqueci o browser"), **não abrir outra
issue** — comentar ou editar a existente.
