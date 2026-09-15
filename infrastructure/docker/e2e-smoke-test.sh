#!/usr/bin/env bash
# Fluxo E2E completo contra a stack Docker Compose (Api + Web + PostgreSQL + LocalStack) já
# em execução — replica manualmente o que foi validado na Etapa 14 (register → login →
# upload metadata → PUT presigned direto no LocalStack → complete → link público → download
# anônimo → conteúdo correto → histórico de download). Usado por .github/workflows/infrastructure.yml
# e por qualquer desenvolvedor local depois de `docker compose up -d`. Não é um substituto dos
# testes automatizados (FileSharing.IntegrationTests já cobre cada peça isoladamente e em
# conjunto) — é uma validação adicional de que a composição real dos containers (rede, variáveis
# de ambiente, endpoint público de presigned URL) continua funcionando.
#
# Uso:
#   cd infrastructure/docker
#   docker compose up -d
#   ./e2e-smoke-test.sh [base_url_api] [base_url_web]
#
# Sai com código != 0 e uma mensagem clara na primeira falha (set -e; nenhuma etapa é ignorada).

set -euo pipefail

API_BASE="${1:-http://localhost:5105}"
WEB_BASE="${2:-http://localhost:5287}"

log() { echo "[e2e] $*"; }
fail() { echo "[e2e] FALHA: $*" >&2; exit 1; }

EMAIL="e2e_$(date +%s)_$$@example.com"
PASSWORD="Sup3rSecret!E2E"

log "Api health/live..."
curl --fail --silent "$API_BASE/health/live" > /dev/null || fail "/health/live não respondeu"

log "Api health/ready..."
curl --fail --silent "$API_BASE/health/ready" > /dev/null || fail "/health/ready não respondeu (Postgres/LocalStack ainda não saudáveis do ponto de vista da Api?)"

log "Web health/live..."
curl --fail --silent "$WEB_BASE/health/live" > /dev/null || fail "Web /health/live não respondeu"

log "Register..."
curl --fail --silent -X POST "$API_BASE/api/auth/register" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}" > /dev/null || fail "register falhou"

log "Login..."
LOGIN_RESPONSE=$(curl --fail --silent -X POST "$API_BASE/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}") || fail "login falhou"
TOKEN=$(echo "$LOGIN_RESPONSE" | python3 -c "import json,sys;print(json.load(sys.stdin)['accessToken'])")
[ -n "$TOKEN" ] || fail "login não retornou accessToken"

log "GET /api/auth/me..."
curl --fail --silent "$API_BASE/api/auth/me" -H "Authorization: Bearer $TOKEN" > /dev/null || fail "GET /me falhou"

log "Initiate upload..."
printf "e2e smoke test content" > /tmp/e2e-smoke-test.zip
SIZE=$(stat -c%s /tmp/e2e-smoke-test.zip 2>/dev/null || stat -f%z /tmp/e2e-smoke-test.zip)
UPLOAD_RESPONSE=$(curl --fail --silent -X POST "$API_BASE/api/files/upload" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"fileName\":\"e2e.zip\",\"contentType\":\"application/zip\",\"sizeBytes\":$SIZE,\"isFolder\":false}") || fail "initiate upload falhou"
FILE_ID=$(echo "$UPLOAD_RESPONSE" | python3 -c "import json,sys;print(json.load(sys.stdin)['fileId'])")
UPLOAD_URL=$(echo "$UPLOAD_RESPONSE" | python3 -c "import json,sys;print(json.load(sys.stdin)['uploadUrl'])")

log "PUT direto na presigned URL (deve ser o host público, não o nome de serviço interno)..."
case "$UPLOAD_URL" in
  http://localstack:*) fail "presigned URL aponta para o host interno do Docker (localstack:4566) — inalcançável daqui. Ver AWS__PublicServiceURL em docker-compose.yml." ;;
esac
curl --fail --silent -X PUT "$UPLOAD_URL" -H "Content-Type: application/zip" --data-binary @/tmp/e2e-smoke-test.zip > /dev/null || fail "PUT presigned falhou"

log "Complete upload..."
curl --fail --silent -X POST "$API_BASE/api/files/$FILE_ID/complete" -H "Authorization: Bearer $TOKEN" > /dev/null || fail "complete falhou"

log "Generate public link..."
LINK_RESPONSE=$(curl --fail --silent -X POST "$API_BASE/api/files/$FILE_ID/link" -H "Authorization: Bearer $TOKEN") || fail "link falhou"
ACCESS_TOKEN=$(echo "$LINK_RESPONSE" | python3 -c "import json,sys;print(json.load(sys.stdin)['accessToken'])")

log "Anonymous access to public metadata..."
curl --fail --silent "$API_BASE/api/public/files/$ACCESS_TOKEN" > /dev/null || fail "acesso público (metadata) falhou"

log "Anonymous download..."
DOWNLOAD_RESPONSE=$(curl --fail --silent "$API_BASE/api/public/files/$ACCESS_TOKEN/download") || fail "acesso público (download) falhou"
DOWNLOAD_URL=$(echo "$DOWNLOAD_RESPONSE" | python3 -c "import json,sys;print(json.load(sys.stdin)['downloadUrl'])")

log "GET do conteúdo real via a presigned download URL..."
DOWNLOADED_CONTENT=$(curl --fail --silent "$DOWNLOAD_URL")
[ "$DOWNLOADED_CONTENT" = "e2e smoke test content" ] || fail "conteúdo baixado não confere com o que foi enviado"

log "Download history (owner)..."
HISTORY=$(curl --fail --silent "$API_BASE/api/files/$FILE_ID/downloads" -H "Authorization: Bearer $TOKEN") || fail "histórico de download falhou"
echo "$HISTORY" | grep -q "downloadedAt" || fail "histórico de download veio vazio"

log "SignalR negotiate (autenticado)..."
curl --fail --silent -X POST "$API_BASE/hubs/notifications/negotiate?negotiateVersion=1" \
  -H "Authorization: Bearer $TOKEN" > /dev/null || fail "negotiate do SignalR falhou"

rm -f /tmp/e2e-smoke-test.zip

log "OK — fluxo E2E completo passou (register, login, upload direto ao S3, complete, link público, download anônimo, histórico, SignalR negotiate)."
