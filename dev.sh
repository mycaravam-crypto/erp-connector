#!/usr/bin/env bash
set -euo pipefail

REPO="$(cd "$(dirname "$0")" && pwd)"
DOTNET="$HOME/.dotnet/dotnet"

B='\033[1m'; BLUE='\033[34m'; GREEN='\033[32m'; R='\033[0m'

mkdir -p "$REPO/src/Connector.Api/staging"

cleanup() { trap - INT TERM EXIT; printf "\n${B}Stopping…${R}\n"; kill 0 2>/dev/null || true; }
trap cleanup INT TERM EXIT

for _port in 5189 5173; do
  lsof -ti:"$_port" 2>/dev/null | xargs -r kill 2>/dev/null || true
done
sleep 1

printf "${B}connector dev${R}  —  Ctrl-C to stop\n"
printf "  ${BLUE}[api]${R}  http://localhost:5189\n"
printf "  ${GREEN}[ui] ${R}  http://localhost:5173\n\n"

"$DOTNET" run --project "$REPO/src/Connector.Api" 2>&1 \
  | sed -u "s/^/$(printf "${BLUE}[api]${R}") /" &

npm --prefix "$REPO/src/connector-ui" run dev 2>&1 \
  | sed -u "s/^/$(printf "${GREEN}[ui] ${R}") /" &

wait
