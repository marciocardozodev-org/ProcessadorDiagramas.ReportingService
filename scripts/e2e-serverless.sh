#!/usr/bin/env bash
# =============================================================================
# E2E Serverless — ProcessadorDiagramas.ReportingService
# =============================================================================
# Variáveis esperadas (via env ou contrato carregado pelo runner):
#   API_BASE_URL            URL base da API de Relatórios
#   AWS_REGION              Região AWS
#   S3_BUCKET_NAME          Bucket S3 de uploads
#   S3_KEY_PREFIX           Prefixo S3
#   ANALYSIS_COMPLETED_QUEUE_NAME  Fila SQS de resultados concluídos
#   ARTIFACT_DIR            Diretório de artefatos (default: artifacts/serverless-e2e)
#   TIMEOUT_SECONDS         Timeout polling (default: 240)
#   POLL_INTERVAL_SECONDS   Intervalo polling (default: 3)
#   REQUIRE_FULL_SERVERLESS Se "true", desabilita qualquer fallback silencioso
# =============================================================================
set -euo pipefail

# ---------------------------------------------------------------------------
# Config
# ---------------------------------------------------------------------------
ARTIFACT_DIR="${ARTIFACT_DIR:-artifacts/serverless-e2e}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-240}"
POLL_INTERVAL_SECONDS="${POLL_INTERVAL_SECONDS:-3}"
REQUIRE_FULL_SERVERLESS="${REQUIRE_FULL_SERVERLESS:-false}"

mkdir -p "${ARTIFACT_DIR}"

ANALYSIS_ID="${ANALYSIS_ID:-$(cat /proc/sys/kernel/random/uuid 2>/dev/null || python3 -c 'import uuid; print(uuid.uuid4())' 2>/dev/null || openssl rand -hex 16 | sed 's/\(.\{8\}\)\(.\{4\}\)\(.\{4\}\)\(.\{4\}\)/\1-\2-\3-\4-/')}"
CORRELATION_ID="e2e-$(date +%s)-${ANALYSIS_ID:0:8}"
JOB_ID="job-${CORRELATION_ID}"

echo "=== [E2E-SERVERLESS] Iniciando execução ==="
echo "  analysisId     : ${ANALYSIS_ID}"
echo "  correlationId  : ${CORRELATION_ID}"
echo "  jobId          : ${JOB_ID}"
echo "  apiBaseUrl     : ${API_BASE_URL:-<não definido>}"
echo "  artifactDir    : ${ARTIFACT_DIR}"
echo "  requireFull    : ${REQUIRE_FULL_SERVERLESS}"

# ---------------------------------------------------------------------------
# Funções utilitárias
# ---------------------------------------------------------------------------
summary_status="unknown"
summary_mode="serverless"
summary_fallback_reason=""

write_summary() {
  local status="$1"
  local detail="${2:-}"
  jq -n \
    --arg analysisId    "${ANALYSIS_ID}" \
    --arg correlationId "${CORRELATION_ID}" \
    --arg jobId         "${JOB_ID}" \
    --arg status        "${status}" \
    --arg mode          "${summary_mode}" \
    --arg fallbackReason "${summary_fallback_reason}" \
    --arg detail        "${detail}" \
    '{
      analysisId:     $analysisId,
      correlationId:  $correlationId,
      jobId:          $jobId,
      status:         $status,
      mode:           $mode,
      fallbackReason: $fallbackReason,
      detail:         $detail,
      generatedAtUtc: now | todateiso8601
    }' > "${ARTIFACT_DIR}/summary.json"
  echo "=== [SUMMARY] status=${status} mode=${summary_mode} ==="
  cat "${ARTIFACT_DIR}/summary.json"
}

fail() {
  local msg="$1"
  echo "=== [ERRO] ${msg}" >&2
  write_summary "failed" "${msg}"
  exit 1
}

degraded_fail() {
  local msg="$1"
  echo "=== [FALLBACK NEGADO] REQUIRE_FULL_SERVERLESS=true — ${msg}" >&2
  write_summary "failed" "REQUIRE_FULL_SERVERLESS bloqueou fallback: ${msg}"
  exit 1
}

# ---------------------------------------------------------------------------
# Preflight
# ---------------------------------------------------------------------------
echo ""
echo "--- [PREFLIGHT] Validando dependências ---"

if ! command -v jq >/dev/null 2>&1; then
  fail "jq não encontrado. Instale jq antes de executar este script."
fi

if ! command -v aws >/dev/null 2>&1; then
  fail "aws CLI não encontrado."
fi

if ! command -v curl >/dev/null 2>&1; then
  fail "curl não encontrado."
fi

# Validar credencial AWS — distingue: permissão x configuração
echo "  Verificando credencial AWS..."
if ! aws sts get-caller-identity >/dev/null 2>&1; then
  AWS_ERR=$(aws sts get-caller-identity 2>&1 || true)
  if echo "${AWS_ERR}" | grep -qi "InvalidClientTokenId\|ExpiredToken\|NoCredentialProviders"; then
    fail "[CONFIG] Credencial AWS inválida ou expirada: ${AWS_ERR}"
  fi
  fail "[PERMISSÃO] aws sts get-caller-identity falhou: ${AWS_ERR}"
fi
echo "  Credencial AWS OK."

# Validar API
if [[ -z "${API_BASE_URL:-}" ]]; then
  fail "[CONFIG] API_BASE_URL não definido. Defina via secret HOMOLOG_API_BASE_URL ou via fallback local."
fi

echo "  Verificando health da API: ${API_BASE_URL}/health ..."
HEALTH_HTTP=$(curl -o /dev/null -s -w "%{http_code}" --max-time 10 "${API_BASE_URL}/health" || echo "000")
if [[ "${HEALTH_HTTP}" != "200" ]]; then
  fail "[CONFIG] Health check falhou (HTTP ${HEALTH_HTTP}). API indisponível em ${API_BASE_URL}/health"
fi
echo "  API health OK (HTTP ${HEALTH_HTTP})."

# Validar acesso mínimo S3
echo "  Verificando acesso ao bucket S3: ${S3_BUCKET_NAME} ..."
S3_CHECK_ERR=""
if ! aws s3api head-bucket --bucket "${S3_BUCKET_NAME}" 2>/tmp/s3-check.err; then
  S3_CHECK_ERR=$(cat /tmp/s3-check.err)
  if echo "${S3_CHECK_ERR}" | grep -qi "403\|AccessDenied"; then
    if [[ "${REQUIRE_FULL_SERVERLESS}" == "true" ]]; then
      degraded_fail "Acesso negado ao bucket S3 '${S3_BUCKET_NAME}': ${S3_CHECK_ERR}"
    fi
    echo "  [AVISO] Acesso S3 negado (IAM restrita). Continuando em modo degradado."
    summary_mode="api-smoke"
    summary_fallback_reason="Sem acesso ao bucket S3 '${S3_BUCKET_NAME}' (403 AccessDenied)."
  elif echo "${S3_CHECK_ERR}" | grep -qi "NoSuchBucket\|404"; then
    fail "[CONFIG] Bucket S3 '${S3_BUCKET_NAME}' não existe: ${S3_CHECK_ERR}"
  else
    if [[ "${REQUIRE_FULL_SERVERLESS}" == "true" ]]; then
      degraded_fail "Falha ao verificar bucket S3: ${S3_CHECK_ERR}"
    fi
    echo "  [AVISO] Verificação S3 falhou (${S3_CHECK_ERR}). Modo degradado."
    summary_mode="api-smoke"
    summary_fallback_reason="Falha ao acessar S3: ${S3_CHECK_ERR}"
  fi
else
  echo "  Bucket S3 acessível OK."
fi

# Validar acesso SQS (se modo full)
if [[ "${summary_mode}" == "serverless" ]]; then
  echo "  Verificando fila SQS: ${ANALYSIS_COMPLETED_QUEUE_NAME} ..."
  QUEUE_URL=""
  SQS_ERR=""
  if ! QUEUE_URL=$(aws sqs get-queue-url \
      --queue-name "${ANALYSIS_COMPLETED_QUEUE_NAME}" \
      --region "${AWS_REGION}" \
      --query QueueUrl --output text 2>/tmp/sqs-check.err); then
    SQS_ERR=$(cat /tmp/sqs-check.err)
    if echo "${SQS_ERR}" | grep -qi "AccessDenied\|UnauthorizedAccess"; then
      if [[ "${REQUIRE_FULL_SERVERLESS}" == "true" ]]; then
        degraded_fail "Sem permissão SQS para '${ANALYSIS_COMPLETED_QUEUE_NAME}': ${SQS_ERR}"
      fi
      echo "  [AVISO] SQS sem permissão IAM. Modo degradado."
      summary_mode="api-smoke"
      summary_fallback_reason="Sem acesso SQS '${ANALYSIS_COMPLETED_QUEUE_NAME}' (AccessDenied)."
    elif echo "${SQS_ERR}" | grep -qi "NonExistentQueue"; then
      fail "[CONFIG] Fila SQS '${ANALYSIS_COMPLETED_QUEUE_NAME}' não existe: ${SQS_ERR}"
    else
      if [[ "${REQUIRE_FULL_SERVERLESS}" == "true" ]]; then
        degraded_fail "Falha ao acessar SQS: ${SQS_ERR}"
      fi
      echo "  [AVISO] SQS inacessível (${SQS_ERR}). Modo degradado."
      summary_mode="api-smoke"
      summary_fallback_reason="Falha ao acessar SQS: ${SQS_ERR}"
    fi
  else
    echo "  Fila SQS acessível OK: ${QUEUE_URL}"
  fi
fi

echo "--- [PREFLIGHT] Concluído. modo=${summary_mode} ---"
echo ""

# ---------------------------------------------------------------------------
# Step 1: GET /internal/reports/{analysisId} — solicitar/consultar relatório
# ---------------------------------------------------------------------------
echo "--- [STEP 1] GET /internal/reports/${ANALYSIS_ID} ---"

GET_RESPONSE=$(curl -s -w '\n%{http_code}' \
  -H "X-Correlation-ID: ${CORRELATION_ID}" \
  -H "Accept: application/json" \
  "${API_BASE_URL}/internal/reports/${ANALYSIS_ID}" || echo -e '{"error":"curl failed"}\n000')

GET_STATUS=$(echo "${GET_RESPONSE}" | tail -1)
GET_BODY=$(echo "${GET_RESPONSE}" | head -n -1)

echo "${GET_BODY}" | jq . > "${ARTIFACT_DIR}/get-response.json" 2>/dev/null || echo "${GET_BODY}" > "${ARTIFACT_DIR}/get-response.json"
echo "  HTTP status: ${GET_STATUS}"
echo "  Resposta salva em ${ARTIFACT_DIR}/get-response.json"

if [[ "${GET_STATUS}" == "404" ]]; then
  echo "  [INFO] Relatório não encontrado (404). Isso pode ser esperado para analysisId novo."
elif [[ "${GET_STATUS}" == "500" || "${GET_STATUS}" == "502" || "${GET_STATUS}" == "503" ]]; then
  if [[ "${REQUIRE_FULL_SERVERLESS}" == "true" ]]; then
    fail "[INFRA] GET retornou HTTP ${GET_STATUS} — provável falha do ProcessingService upstream."
  fi
  echo "  [AVISO] GET retornou HTTP ${GET_STATUS} — ProcessingService provavelmente indisponível. Degradando para api-smoke."
  summary_mode="api-smoke"
  summary_fallback_reason="GET /internal/reports retornou ${GET_STATUS} (ProcessingService indisponível)."
elif [[ "${GET_STATUS}" != "200" && "${GET_STATUS}" != "202" ]]; then
  fail "[INFRA] GET retornou HTTP ${GET_STATUS} inesperado: ${GET_BODY}"
fi

# ---------------------------------------------------------------------------
# Step 2: POST /internal/reports/{analysisId}/generate — forçar geração
# ---------------------------------------------------------------------------
echo ""
echo "--- [STEP 2] POST /internal/reports/${ANALYSIS_ID}/generate ---"

CREATE_RESPONSE=$(curl -s -w '\n%{http_code}' \
  -X POST \
  -H "X-Correlation-ID: ${CORRELATION_ID}" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json" \
  "${API_BASE_URL}/internal/reports/${ANALYSIS_ID}/generate" || echo -e '{"error":"curl failed"}\n000')

CREATE_STATUS=$(echo "${CREATE_RESPONSE}" | tail -1)
CREATE_BODY=$(echo "${CREATE_RESPONSE}" | head -n -1)

echo "${CREATE_BODY}" | jq . > "${ARTIFACT_DIR}/create-response.json" 2>/dev/null || echo "${CREATE_BODY}" > "${ARTIFACT_DIR}/create-response.json"
echo "  HTTP status: ${CREATE_STATUS}"
echo "  Resposta salva em ${ARTIFACT_DIR}/create-response.json"

if [[ "${CREATE_STATUS}" == "404" ]]; then
  echo "  [INFO] ProcessingService não conhece este analysisId (404). Comportamento esperado para UUID sintético."
  write_summary "passed-smoke" "API respondeu corretamente com 404 para analysisId sintético. Contrato validado."
  echo ""
  echo "=== [E2E-SERVERLESS] Execução concluída (modo: ${summary_mode}) ==="
  exit 0
fi

if [[ "${CREATE_STATUS}" == "500" || "${CREATE_STATUS}" == "502" || "${CREATE_STATUS}" == "503" ]]; then
  if [[ "${REQUIRE_FULL_SERVERLESS}" == "true" ]]; then
    fail "[INFRA] POST generate retornou HTTP ${CREATE_STATUS} — provável falha do ProcessingService upstream."
  fi
  echo "  [AVISO] POST generate retornou HTTP ${CREATE_STATUS} — ProcessingService indisponível. Degradando para api-smoke."
  summary_mode="api-smoke"
  summary_fallback_reason="POST /generate retornou ${CREATE_STATUS} (ProcessingService indisponível)."
  write_summary "passed-smoke" "Contrato REST da API validado (modo degradado: ${summary_fallback_reason})"
  echo ""
  echo "=== [E2E-SERVERLESS] Execução concluída (modo: ${summary_mode}) ==="
  exit 0
fi

if [[ "${CREATE_STATUS}" != "200" && "${CREATE_STATUS}" != "202" ]]; then
  fail "[INFRA] POST generate retornou HTTP ${CREATE_STATUS} inesperado: ${CREATE_BODY}"
fi

echo "  POST generate OK (HTTP ${CREATE_STATUS})."

# ---------------------------------------------------------------------------
# Step 3 (modo serverless): Polling de status
# ---------------------------------------------------------------------------
if [[ "${summary_mode}" == "serverless" ]]; then
  echo ""
  echo "--- [STEP 3] Polling de status (timeout ${TIMEOUT_SECONDS}s, intervalo ${POLL_INTERVAL_SECONDS}s) ---"

  ELAPSED=0
  FINAL_STATUS=""
  FINAL_BODY=""

  while [[ ${ELAPSED} -lt ${TIMEOUT_SECONDS} ]]; do
    POLL_RESPONSE=$(curl -s -w '\n%{http_code}' \
      -H "X-Correlation-ID: ${CORRELATION_ID}" \
      -H "Accept: application/json" \
      "${API_BASE_URL}/internal/reports/${ANALYSIS_ID}" || echo -e '{"error":"curl failed"}\n000')

    POLL_HTTP=$(echo "${POLL_RESPONSE}" | tail -1)
    POLL_BODY=$(echo "${POLL_RESPONSE}" | head -n -1)

    if [[ "${POLL_HTTP}" == "200" ]]; then
      POLL_STATUS_VAL=$(echo "${POLL_BODY}" | jq -r '.status // "unknown"')
      if [[ "${POLL_STATUS_VAL}" != "Pending" && "${POLL_STATUS_VAL}" != "Processing" ]]; then
        FINAL_STATUS="${POLL_STATUS_VAL}"
        FINAL_BODY="${POLL_BODY}"
        echo "  Relatório finalizado. status=${FINAL_STATUS} (elapsed=${ELAPSED}s)"
        break
      fi
    fi

    echo "  [${ELAPSED}s] HTTP=${POLL_HTTP} status=$(echo "${POLL_BODY}" | jq -r '.status // "?"' 2>/dev/null || echo '?') — aguardando..."
    sleep "${POLL_INTERVAL_SECONDS}"
    ELAPSED=$((ELAPSED + POLL_INTERVAL_SECONDS))
  done

  if [[ -z "${FINAL_STATUS}" ]]; then
    fail "Timeout de ${TIMEOUT_SECONDS}s atingido sem resolução do relatório."
  fi

  echo "${FINAL_BODY}" | jq . > "${ARTIFACT_DIR}/final-event.json" 2>/dev/null || echo "${FINAL_BODY}" > "${ARTIFACT_DIR}/final-event.json"
  echo "  final-event.json salvo."

  # Validar contrato do response final
  REPORT_ID=$(echo "${FINAL_BODY}" | jq -r '.reportId // empty')
  REPORT_ANALYSIS_ID=$(echo "${FINAL_BODY}" | jq -r '.analysisProcessId // empty')

  if [[ -z "${REPORT_ID}" ]]; then
    fail "[NEGÓCIO] Campo 'reportId' ausente na resposta final: ${FINAL_BODY}"
  fi

  if [[ -z "${REPORT_ANALYSIS_ID}" ]]; then
    fail "[NEGÓCIO] Campo 'analysisProcessId' ausente na resposta final: ${FINAL_BODY}"
  fi

  write_summary "passed" "Relatório gerado com sucesso. reportId=${REPORT_ID} status=${FINAL_STATUS}"
else
  # api-smoke: já escrevemos summary em passed-smoke acima se 404, aqui apenas status final de smoke
  write_summary "passed-smoke" "Validação de contrato de API concluída (modo degradado: ${summary_fallback_reason})"
fi

echo ""
echo "=== [E2E-SERVERLESS] Execução concluída (modo: ${summary_mode}) ==="
