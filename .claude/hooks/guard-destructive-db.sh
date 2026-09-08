#!/usr/bin/env bash
# Bloqueia comandos que destroem o DB de dev (volume pgdata) ou o schema.
# Referência: CLAUDE.md, seção "Comandos destrutivos no DB — bloqueados".
#
# Padrões cobertos:
#   dotnet ef database drop
#   docker compose down -v / --volumes
#   docker volume rm <volume do projeto>
#   psql com DROP DATABASE / DROP TABLE / TRUNCATE
#
# Não há critério de liberação: testes usam SQLite in-memory e nunca precisam
# derrubar o Postgres. Precisando de drop real, o dev roda manualmente.

set -euo pipefail

input=$(cat)

cmd=$(printf '%s' "$input" | python3 -c '
import json, sys
try:
    payload = json.load(sys.stdin)
except Exception:
    sys.exit(0)
print(payload.get("tool_input", {}).get("command", ""))
')

if [ -z "$cmd" ]; then
  exit 0
fi

destructive=0

if printf '%s' "$cmd" | grep -qE 'dotnet[[:space:]]+ef[[:space:]]+database[[:space:]]+drop'; then
  destructive=1
fi

if printf '%s' "$cmd" | grep -qE 'docker[[:space:]]+compose[[:space:]]+down[^|;&]*([[:space:]]-v([[:space:]]|$)|--volumes)'; then
  destructive=1
fi

if printf '%s' "$cmd" | grep -qE 'docker[[:space:]]+volume[[:space:]]+(rm|prune)'; then
  destructive=1
fi

if printf '%s' "$cmd" | grep -qiE 'psql[^|;&]*-c[[:space:]]*.?(drop[[:space:]]+(database|table|schema)|truncate)'; then
  destructive=1
fi

if [ "$destructive" -eq 0 ]; then
  exit 0
fi

cat >&2 <<EOF
🛑 Comando destrutivo de DB bloqueado.

Detectado:
  $cmd

Esse comando apaga o DB de dev (volume pgdata) ou o schema. Não há motivo
legítimo pra um agente fazer isso: os testes rodam em SQLite in-memory e
mudança de schema é migration incremental nova (dotnet ef migrations add).

Se schema local divergiu do código, a saída é criar migration nova — nunca
drop. Precisando dropar de verdade, rode manualmente fora do Claude.
EOF

exit 2
