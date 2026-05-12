#!/usr/bin/env bash
# =============================================================================
# AWS Cleanup — Remove recursos com custo (S3, SQS, RDS, etc)
# =============================================================================
# Uso:
#   ./scripts/aws-cleanup.sh [--confirm] [--capture-state] [--include-rds]
#
# Sem --confirm, lista o que será removido. Com --confirm, processa.
# --capture-state salva um snapshot local com informações úteis para retomada.
# --include-rds habilita limpeza de RDS dedicada; por segurança, RDS compartilhada
# não é removida por padrão.
# =============================================================================
set -euo pipefail

CONFIRM=""
CAPTURE_STATE="false"
INCLUDE_RDS="false"
AWS_REGION="${AWS_REGION:-us-east-1}"
K8S_NAMESPACE="${K8S_NAMESPACE:-homolog}"
STATE_DIR="${STATE_DIR:-artifacts/end-of-day}"

for arg in "$@"; do
  case "$arg" in
    --confirm)
      CONFIRM="--confirm"
      ;;
    --capture-state)
      CAPTURE_STATE="true"
      ;;
    --include-rds)
      INCLUDE_RDS="true"
      ;;
    *)
      echo "Argumento não suportado: $arg" >&2
      exit 1
      ;;
  esac
done

echo "=== AWS Cleanup — Região: ${AWS_REGION} ==="
echo ""

capture_state() {
  mkdir -p "${STATE_DIR}"

  cat > "${STATE_DIR}/restart-notes.txt" <<EOF
AWS_REGION=${AWS_REGION}
K8S_NAMESPACE=${K8S_NAMESPACE}
START_OF_DAY:
  1. Exportar novas credenciais AWS Academy
  2. AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
  3. AWS_ACCOUNT_ID=\${AWS_ACCOUNT_ID} ./scripts/aws-setup-resources.sh
  4. Recriar/atualizar GitHub secrets AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_SESSION_TOKEN
  5. Garantir secret HOMOLOG_DB_CONNECTION_STRING no GitHub
  6. Rodar pipeline ci-cd na branch homolog
  7. Rodar workflow manual Homolog E2E Isolado — ReportingService

DB_REPORTING:
  host=processador-diagramas-pg-hml.cd26cy06k3ng.us-east-1.rds.amazonaws.com
  database=processador_diagramas_reporting
  username=reporting_service
  secret_manager=/homolog/reportingservice/db-credentials

MASTER_SECRET:
  secret_manager=/homolog/shared-rds/master-credentials

WORKFLOWS:
  - .github/workflows/ci-cd.yml
  - .github/workflows/homolog-e2e-isolated.yml
EOF

  {
    echo "{"
    echo "  \"generatedAt\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"," 
    echo "  \"awsRegion\": \"${AWS_REGION}\"," 
    echo "  \"k8sNamespace\": \"${K8S_NAMESPACE}\""
    echo "}"
  } > "${STATE_DIR}/state.json"

  echo "Snapshot salvo em ${STATE_DIR}/"
}

if [[ "${CAPTURE_STATE}" == "true" ]]; then
  echo "--- [STATE] Capturando snapshot local para retomada ---"
  capture_state
  echo ""
fi

# ---------------------------------------------------------------------------
# Função auxiliar
# ---------------------------------------------------------------------------
prompt_confirm() {
  local msg="$1"
  if [[ "${CONFIRM}" != "--confirm" ]]; then
    echo "[DRY-RUN] $msg"
    return 1
  fi
  echo "[EXECUTANDO] $msg"
  return 0
}

# ---------------------------------------------------------------------------
# 1. Limpar SQS queues
# ---------------------------------------------------------------------------
echo "--- [SQS] Listando e limpando filas ---"
QUEUES=$(aws sqs list-queues --region "${AWS_REGION}" --query 'QueueUrls[?contains(QueueUrl, `processador-diagramas`) || contains(QueueUrl, `upload-orchestrator`)]' --output text 2>/dev/null || echo "")

if [[ -z "${QUEUES}" ]]; then
  echo "  Nenhuma fila encontrada."
else
  for QUEUE_URL in ${QUEUES}; do
    QUEUE_NAME=$(basename "${QUEUE_URL}")
    if prompt_confirm "Deletar fila SQS: ${QUEUE_NAME}"; then
      aws sqs delete-queue --queue-url "${QUEUE_URL}" --region "${AWS_REGION}"
      echo "  ✓ Fila deletada: ${QUEUE_NAME}"
    fi
  done
fi

# ---------------------------------------------------------------------------
# 2. Limpar S3 buckets
# ---------------------------------------------------------------------------
echo ""
echo "--- [S3] Listando e limpando buckets ---"
BUCKETS=$(aws s3api list-buckets --query 'Buckets[?contains(Name, `processador-diagramas`)].Name' --output text)

if [[ -z "${BUCKETS}" ]]; then
  echo "  Nenhum bucket encontrado."
else
  for BUCKET in ${BUCKETS}; do
    echo "  Bucket: ${BUCKET}"
    
    # Limpar versões e deletar objetos
    if prompt_confirm "Limpar objetos do bucket: ${BUCKET}"; then
      aws s3 rm "s3://${BUCKET}" --recursive --region "${AWS_REGION}" 2>/dev/null || true

      # Deletar versões antigas apenas quando houver versionamento/markers.
      DELETE_MARKERS_JSON="$(aws s3api list-object-versions \
        --bucket "${BUCKET}" \
        --query 'DeleteMarkers[].{Key:Key,VersionId:VersionId}' \
        --output json 2>/dev/null || echo '[]')"

      if [[ "${DELETE_MARKERS_JSON}" != "[]" && -n "${DELETE_MARKERS_JSON}" ]]; then
        DELETE_PAYLOAD="$(echo "${DELETE_MARKERS_JSON}" | jq '{Objects: ., Quiet: true}')"
        aws s3api delete-objects \
          --bucket "${BUCKET}" \
          --delete "${DELETE_PAYLOAD}" \
          --region "${AWS_REGION}" 2>/dev/null || true
      fi
      
      echo "  ✓ Objetos deletados: ${BUCKET}"
    fi
    
    # Deletar bucket
    if prompt_confirm "Deletar bucket S3: ${BUCKET}"; then
      aws s3api delete-bucket --bucket "${BUCKET}" --region "${AWS_REGION}" 2>/dev/null || \
      aws s3api delete-bucket --bucket "${BUCKET}" --region "${AWS_REGION}" --delete-bucket-configuration LocationConstraint="${AWS_REGION}" 2>/dev/null || true
      echo "  ✓ Bucket deletado: ${BUCKET}"
    fi
  done
fi

# ---------------------------------------------------------------------------
# 3. Limpar recursos do ReportingService no EKS
# ---------------------------------------------------------------------------
echo ""
echo "--- [EKS] Limpando recursos do ReportingService no namespace ${K8S_NAMESPACE} ---"

K8S_RESOURCES=(
  "deployment/processador-diagramas-reportingservice"
  "service/processador-diagramas-reportingservice"
  "job/processador-diagramas-reportingservice-migrations"
  "secret/processador-diagramas-reportingservice-secrets"
  "configmap/processador-diagramas-reportingservice-config"
)

for RESOURCE in "${K8S_RESOURCES[@]}"; do
  if ! kubectl get "${RESOURCE}" -n "${K8S_NAMESPACE}" >/dev/null 2>&1; then
    echo "  Recurso ausente: ${RESOURCE}"
    continue
  fi

  if prompt_confirm "Deletar recurso K8s: ${RESOURCE}"; then
    kubectl delete "${RESOURCE}" -n "${K8S_NAMESPACE}" --ignore-not-found=true >/dev/null 2>&1 || true
    echo "  ✓ Recurso removido: ${RESOURCE}"
  fi
done

# ---------------------------------------------------------------------------
# 4. Limpar RDS (somente quando explicitamente habilitado)
# ---------------------------------------------------------------------------
echo ""
echo "--- [RDS] Listando e limpando instâncias ---"
if [[ "${INCLUDE_RDS}" != "true" ]]; then
  echo "  RDS ignorada por segurança. Use --include-rds se quiser listar/remover instâncias dedicadas."
  echo "  Observação: a instância compartilhada processador-diagramas-pg-hml não deve ser removida por padrão."
else
  RDS_INSTANCES=$(aws rds describe-db-instances --region "${AWS_REGION}" --query "DBInstances[?DBInstanceIdentifier=='pd-processingservice-pg-hml'].DBInstanceIdentifier" --output text 2>/dev/null || echo "")

  if [[ -z "${RDS_INSTANCES}" ]]; then
    echo "  Nenhuma instância RDS dedicada encontrada para remoção automática."
  else
    for DB in ${RDS_INSTANCES}; do
      if prompt_confirm "Deletar RDS instance dedicada: ${DB}"; then
        aws rds delete-db-instance \
          --db-instance-identifier "${DB}" \
          --skip-final-snapshot \
          --region "${AWS_REGION}" 2>/dev/null || true
        echo "  ✓ RDS deletada: ${DB}"
      fi
    done
  fi
fi

# ---------------------------------------------------------------------------
# 5. Limpar ECR repositories
# ---------------------------------------------------------------------------
echo ""
echo "--- [ECR] Listando e limpando repositórios ---"
ECR_REPOS=$(aws ecr describe-repositories --region "${AWS_REGION}" --query "repositories[?contains(repositoryName, 'processador') || contains(repositoryName, 'reporting')].repositoryName" --output text 2>/dev/null || echo "")

if [[ -z "${ECR_REPOS}" ]]; then
  echo "  Nenhum repositório ECR encontrado."
else
  for REPO in ${ECR_REPOS}; do
    if prompt_confirm "Deletar ECR repository: ${REPO}"; then
      aws ecr delete-repository \
        --repository-name "${REPO}" \
        --force \
        --region "${AWS_REGION}" 2>/dev/null || true
      echo "  ✓ ECR deletada: ${REPO}"
    fi
  done
fi

# ---------------------------------------------------------------------------
# Resumo
# ---------------------------------------------------------------------------
echo ""
if [[ "${CONFIRM}" != "--confirm" ]]; then
  echo "=== DRY-RUN Completo ==="
  echo "Execute novamente com --confirm para deletar recursos:"
  echo "  ./scripts/aws-cleanup.sh --capture-state --confirm"
else
  echo "=== Cleanup Completo ==="
  echo "Recursos recriáveis e deploy do ReportingService foram removidos."
fi
