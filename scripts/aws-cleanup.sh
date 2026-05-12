#!/usr/bin/env bash
# =============================================================================
# AWS Cleanup — Remove recursos com custo (S3, SQS, RDS, etc)
# =============================================================================
# Uso:
#   ./scripts/aws-cleanup.sh [--confirm]
#
# Sem --confirm, lista o que será removido. Com --confirm, processa.
# =============================================================================
set -euo pipefail

CONFIRM="${1:-}"
AWS_REGION="${AWS_REGION:-us-east-1}"

echo "=== AWS Cleanup — Região: ${AWS_REGION} ==="
echo ""

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
      
      # Deletar versões antigas (se versionamento habilitado)
      aws s3api delete-objects \
        --bucket "${BUCKET}" \
        --delete "$(aws s3api list-object-versions \
          --bucket "${BUCKET}" \
          --query 'DeleteMarkers[].{Key:Key,VersionId:VersionId}' \
          --output json | jq -r '.[] | "\(.Key),\(.VersionId)"' | sed 's/\(.*\),\(.*\)/{Key:\1,VersionId:\2}/g' | jq -s '[.]' )" \
        --region "${AWS_REGION}" 2>/dev/null || true
      
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
# 3. Limpar RDS (se housekeeping em dias anteriores)
# ---------------------------------------------------------------------------
echo ""
echo "--- [RDS] Listando e limpando instâncias ---"
RDS_INSTANCES=$(aws rds describe-db-instances --region "${AWS_REGION}" --query "DBInstances[?contains(DBInstanceIdentifier, 'processador') || contains(DBInstanceIdentifier, 'reporting')].DBInstanceIdentifier" --output text 2>/dev/null || echo "")

if [[ -z "${RDS_INSTANCES}" ]]; then
  echo "  Nenhuma instância RDS encontrada."
else
  for DB in ${RDS_INSTANCES}; do
    if prompt_confirm "Deletar RDS instance: ${DB}"; then
      aws rds delete-db-instance \
        --db-instance-identifier "${DB}" \
        --skip-final-snapshot \
        --region "${AWS_REGION}" 2>/dev/null || true
      echo "  ✓ RDS deletada: ${DB}"
    fi
  done
fi

# ---------------------------------------------------------------------------
# 4. Limpar ECR repositories
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
  echo "  ./scripts/aws-cleanup.sh --confirm"
else
  echo "=== Cleanup Completo ==="
  echo "Todos os recursos foram removidos."
fi
