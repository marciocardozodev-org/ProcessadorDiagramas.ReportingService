#!/usr/bin/env bash
# =============================================================================
# E2E Isolado — ProcessadorDiagramas.ReportingService
# =============================================================================
# Valida o papel do ReportingService de forma completamente isolada:
#   1. Publica mensagem mock na fila SQS (simula o ProcessingService)
#   2. Aguarda o serviço consumir e persistir o relatório
#   3. Consulta a API via port-forward e valida a resposta
#
# Variáveis esperadas:
#   AWS_REGION                    Região AWS (default: us-east-1)
#   ANALYSIS_COMPLETED_QUEUE_NAME Nome da fila SQS (default: upload-orchestrator-analysis-completed)
#   K8S_NAMESPACE                 Namespace K8s onde o serviço está rodando (default: homolog)
#   K8S_SERVICE_NAME              Nome do Service K8s (default: processador-diagramas-reportingservice)
#   LOCAL_PORT                    Porta local para port-forward (default: 18080)
#   TIMEOUT_SECONDS               Timeout de polling em segundos (default: 60)
#   POLL_INTERVAL_SECONDS         Intervalo de polling em segundos (default: 3)
#   ARTIFACT_DIR                  Diretório para artefatos de saída (default: artifacts/e2e-isolated)
# =============================================================================
set -euo pipefail

# ---------------------------------------------------------------------------
# Config
# ---------------------------------------------------------------------------
AWS_REGION="${AWS_REGION:-us-east-1}"
ANALYSIS_COMPLETED_QUEUE_NAME="${ANALYSIS_COMPLETED_QUEUE_NAME:-upload-orchestrator-analysis-completed}"
K8S_NAMESPACE="${K8S_NAMESPACE:-homolog}"
K8S_SERVICE_NAME="${K8S_SERVICE_NAME:-processador-diagramas-reportingservice}"
LOCAL_PORT="${LOCAL_PORT:-18080}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-60}"
POLL_INTERVAL_SECONDS="${POLL_INTERVAL_SECONDS:-3}"
ARTIFACT_DIR="${ARTIFACT_DIR:-artifacts/e2e-isolated}"

mkdir -p "${ARTIFACT_DIR}"

ANALYSIS_PROCESS_ID="$(cat /proc/sys/kernel/random/uuid 2>/dev/null \
  || python3 -c 'import uuid; print(uuid.uuid4())' 2>/dev/null \
  || openssl rand -hex 16 | sed 's/\(.\{8\}\)\(.\{4\}\)\(.\{4\}\)\(.\{4\}\)/\1-\2-\3-\4-/')"
CORRELATION_ID="e2e-isolated-$(date +%s)-${ANALYSIS_PROCESS_ID:0:8}"
SOURCE_REFERENCE="e2e-job-${ANALYSIS_PROCESS_ID:0:8}"

PFORWARD_PID=""

# ---------------------------------------------------------------------------
# Cleanup
# ---------------------------------------------------------------------------
cleanup() {
  if [[ -n "${PFORWARD_PID}" ]]; then
    echo "  [CLEANUP] Encerrando port-forward (PID ${PFORWARD_PID})..."
    kill "${PFORWARD_PID}" 2>/dev/null || true
  fi
}
trap cleanup EXIT

# ---------------------------------------------------------------------------
# Funções utilitárias
# ---------------------------------------------------------------------------
fail() {
  echo "" >&2
  echo "=== [ERRO] $1" >&2
  echo '{"status":"failed","detail":"'"$1"'"}' > "${ARTIFACT_DIR}/summary.json"
  exit 1
}

pass() {
  echo "" 
  echo "=== [OK] $1"
  echo '{"status":"passed","analysisProcessId":"'"${ANALYSIS_PROCESS_ID}"'","detail":"'"$1"'"}' > "${ARTIFACT_DIR}/summary.json"
}

# ---------------------------------------------------------------------------
# Preflight
# ---------------------------------------------------------------------------
echo "=== [E2E-ISOLATED] Iniciando validação isolada do ReportingService ==="
echo "  analysisProcessId : ${ANALYSIS_PROCESS_ID}"
echo "  correlationId     : ${CORRELATION_ID}"
echo "  sourceReference   : ${SOURCE_REFERENCE}"
echo "  fila SQS          : ${ANALYSIS_COMPLETED_QUEUE_NAME}"
echo "  namespace K8s     : ${K8S_NAMESPACE}"
echo "  serviço K8s       : ${K8S_SERVICE_NAME}"
echo ""

for cmd in aws kubectl curl jq; do
  command -v "$cmd" >/dev/null 2>&1 || fail "Dependência não encontrada: $cmd"
done

echo "--- [PREFLIGHT] Validando credencial AWS..."
aws sts get-caller-identity >/dev/null 2>&1 || fail "Credencial AWS inválida ou expirada."
echo "  Credencial AWS OK."

echo "--- [PREFLIGHT] Verificando fila SQS..."
QUEUE_URL="$(aws sqs get-queue-url \
  --queue-name "${ANALYSIS_COMPLETED_QUEUE_NAME}" \
  --region "${AWS_REGION}" \
  --query QueueUrl --output text 2>/dev/null)" \
  || fail "Fila SQS '${ANALYSIS_COMPLETED_QUEUE_NAME}' não encontrada ou sem permissão."
echo "  Fila SQS OK: ${QUEUE_URL}"

echo "--- [PREFLIGHT] Verificando pod no cluster..."
kubectl get svc "${K8S_SERVICE_NAME}" -n "${K8S_NAMESPACE}" >/dev/null 2>&1 \
  || fail "Service '${K8S_SERVICE_NAME}' não encontrado no namespace '${K8S_NAMESPACE}'."
echo "  Service K8s OK."
echo ""

# ---------------------------------------------------------------------------
# Step 1: Port-forward para acessar a API
# ---------------------------------------------------------------------------
echo "--- [STEP 1] Iniciando port-forward ${LOCAL_PORT} -> ${K8S_SERVICE_NAME}:80 ---"
kubectl port-forward \
  "svc/${K8S_SERVICE_NAME}" \
  "${LOCAL_PORT}:80" \
  -n "${K8S_NAMESPACE}" \
  >/dev/null 2>&1 &
PFORWARD_PID=$!

# Aguardar port-forward estar pronto
for i in {1..10}; do
  sleep 1
  if curl -s --max-time 2 "http://localhost:${LOCAL_PORT}/health" >/dev/null 2>&1; then
    echo "  Port-forward ativo (tentativa ${i}/10). Health OK."
    break
  fi
  if [[ $i -eq 10 ]]; then
    fail "Port-forward não ficou disponível após 10 tentativas."
  fi
done
echo ""

# ---------------------------------------------------------------------------
# Step 2: Confirmar que o analysisProcessId ainda não existe (estado inicial)
# ---------------------------------------------------------------------------
echo "--- [STEP 2] GET /internal/reports/${ANALYSIS_PROCESS_ID} (deve ser 404) ---"
INITIAL_STATUS="$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 \
  -H "X-Correlation-ID: ${CORRELATION_ID}" \
  "http://localhost:${LOCAL_PORT}/internal/reports/${ANALYSIS_PROCESS_ID}")"

if [[ "${INITIAL_STATUS}" == "200" ]]; then
  fail "UUID gerado já existe na base (colisão improvável). Tente novamente."
fi
echo "  HTTP ${INITIAL_STATUS} — UUID ainda não existe. Comportamento esperado."
echo ""

# ---------------------------------------------------------------------------
# Step 3: Publicar mensagem mock na fila SQS
# ---------------------------------------------------------------------------
RAW_AI_OUTPUT='{"components":["API","Worker","Database"],"risks":["sync dependency","single point of failure"],"summary":"E2E isolated test payload"}'

SQS_MESSAGE_BODY="$(jq -n \
  --arg eventType   "AnalysisProcessingCompletedV2" \
  --arg processId   "${ANALYSIS_PROCESS_ID}" \
  --arg rawOutput   "${RAW_AI_OUTPUT}" \
  --arg sourceRef   "${SOURCE_REFERENCE}" \
  --arg corrId      "${CORRELATION_ID}" \
  '{
    EventType:              $eventType,
    AnalysisProcessId:      $processId,
    RawAiOutput:            $rawOutput,
    SourceAnalysisReference: $sourceRef,
    CorrelationId:          $corrId
  }')"

echo "--- [STEP 3] Publicando mensagem mock na fila SQS ---"
echo "${SQS_MESSAGE_BODY}" | jq . > "${ARTIFACT_DIR}/mock-message.json"

SQS_SEND_RESULT="$(aws sqs send-message \
  --queue-url "${QUEUE_URL}" \
  --message-body "${SQS_MESSAGE_BODY}" \
  --region "${AWS_REGION}" \
  --output json)"

MESSAGE_ID="$(echo "${SQS_SEND_RESULT}" | jq -r '.MessageId')"
echo "  Mensagem publicada. MessageId: ${MESSAGE_ID}"
echo "${SQS_SEND_RESULT}" > "${ARTIFACT_DIR}/sqs-send-result.json"
echo ""

# ---------------------------------------------------------------------------
# Step 4: Polling — aguardar o serviço consumir e persistir
# ---------------------------------------------------------------------------
echo "--- [STEP 4] Aguardando ReportingService processar a mensagem (timeout: ${TIMEOUT_SECONDS}s) ---"
START_TIME="$(date +%s)"
REPORT_STATUS=""
GET_BODY=""

while true; do
  ELAPSED=$(( $(date +%s) - START_TIME ))
  if [[ ${ELAPSED} -ge ${TIMEOUT_SECONDS} ]]; then
    fail "Timeout após ${TIMEOUT_SECONDS}s. O serviço não processou a mensagem a tempo."
  fi

  GET_RESPONSE="$(curl -s -w '\n%{http_code}' --max-time 5 \
    -H "X-Correlation-ID: ${CORRELATION_ID}" \
    -H "Accept: application/json" \
    "http://localhost:${LOCAL_PORT}/internal/reports/${ANALYSIS_PROCESS_ID}" 2>/dev/null \
    || echo -e '{}\n000')"

  HTTP_STATUS="$(echo "${GET_RESPONSE}" | tail -1)"
  GET_BODY="$(echo "${GET_RESPONSE}" | head -n -1)"

  if [[ "${HTTP_STATUS}" == "200" ]]; then
    REPORT_STATUS="$(echo "${GET_BODY}" | jq -r '.status // "unknown"')"
    echo "  [${ELAPSED}s] HTTP 200 — status do relatório: ${REPORT_STATUS}"
    if [[ "${REPORT_STATUS}" != "Pending" ]]; then
      break
    fi
  elif [[ "${HTTP_STATUS}" == "202" ]]; then
    echo "  [${ELAPSED}s] HTTP 202 — processamento em andamento..."
  elif [[ "${HTTP_STATUS}" == "404" ]]; then
    echo "  [${ELAPSED}s] HTTP 404 — ainda não consumido..."
  else
    echo "  [${ELAPSED}s] HTTP ${HTTP_STATUS} inesperado — aguardando..."
  fi

  sleep "${POLL_INTERVAL_SECONDS}"
done
echo ""

# ---------------------------------------------------------------------------
# Step 5: Validar conteúdo do relatório
# ---------------------------------------------------------------------------
echo "--- [STEP 5] Validando conteúdo do relatório ---"
echo "${GET_BODY}" | jq . > "${ARTIFACT_DIR}/report-response.json"
echo "  Resposta salva em ${ARTIFACT_DIR}/report-response.json"

RETURNED_PROCESS_ID="$(echo "${GET_BODY}" | jq -r '.analysisProcessId // .AnalysisProcessId // ""')"
RETURNED_STATUS="$(echo "${GET_BODY}" | jq -r '.status // .Status // ""')"

if [[ -z "${RETURNED_PROCESS_ID}" && -z "${RETURNED_STATUS}" ]]; then
  fail "Resposta sem campos esperados (analysisProcessId/status). Verifique ${ARTIFACT_DIR}/report-response.json"
fi

echo "  analysisProcessId retornado : ${RETURNED_PROCESS_ID}"
echo "  status retornado            : ${RETURNED_STATUS}"
echo ""

# ---------------------------------------------------------------------------
# Resultado final
# ---------------------------------------------------------------------------
pass "ReportingService processou a mensagem SQS mock e respondeu corretamente via API. status=${RETURNED_STATUS}"
echo "  Artefatos salvos em: ${ARTIFACT_DIR}/"
echo ""
echo "=== [E2E-ISOLATED] Concluído com sucesso ==="
