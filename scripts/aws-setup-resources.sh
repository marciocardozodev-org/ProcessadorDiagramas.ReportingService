#!/usr/bin/env bash
# =============================================================================
# AWS Setup — Criar recursos (S3, SQS, ECR) para novo dia de trabalho
# =============================================================================
# Uso:
#   AWS_ACCOUNT_ID=<sua-conta> ./scripts/aws-setup-resources.sh
#
# Cria buckets S3, filas SQS e repositório ECR necessários.
# =============================================================================
set -euo pipefail

AWS_REGION="${AWS_REGION:-us-east-1}"
AWS_ACCOUNT_ID="${AWS_ACCOUNT_ID:-}"

if [[ -z "${AWS_ACCOUNT_ID}" ]]; then
  echo "[INFO] AWS_ACCOUNT_ID não definido. Obtendo via STS..."
  AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
fi

echo "=== AWS Setup — Conta: ${AWS_ACCOUNT_ID}, Região: ${AWS_REGION} ==="
echo ""

# ---------------------------------------------------------------------------
# 1. Criar S3 buckets
# ---------------------------------------------------------------------------
echo "--- [S3] Criando buckets ---"

S3_BUCKETS=(
  "processador-diagramas-processingservice-hml-inputs-${AWS_ACCOUNT_ID}"
  "processador-diagramas-reporting-hml-artifacts-${AWS_ACCOUNT_ID}"
)

for BUCKET in "${S3_BUCKETS[@]}"; do
  if aws s3api head-bucket --bucket "${BUCKET}" --region "${AWS_REGION}" 2>/dev/null; then
    echo "  ✓ Bucket já existe: ${BUCKET}"
  else
    echo "  Criando: ${BUCKET}"
    if [[ "${AWS_REGION}" == "us-east-1" ]]; then
      aws s3api create-bucket \
        --bucket "${BUCKET}" \
        --region "${AWS_REGION}" 2>/dev/null || true
    else
      aws s3api create-bucket \
        --bucket "${BUCKET}" \
        --region "${AWS_REGION}" \
        --create-bucket-configuration LocationConstraint="${AWS_REGION}" 2>/dev/null || true
    fi
    echo "  ✓ Bucket criado: ${BUCKET}"
  fi
done

# ---------------------------------------------------------------------------
# 2. Criar SQS queues
# ---------------------------------------------------------------------------
echo ""
echo "--- [SQS] Criando filas ---"

SQS_QUEUES=(
  "processador-diagramas-processingservice-hml-queue"
  "processador-diagramas-processingservice-input"
  "upload-orchestrator-analysis-completed"
  "upload-orchestrator-analysis-requested"
)

for QUEUE in "${SQS_QUEUES[@]}"; do
  QUEUE_URL=$(aws sqs get-queue-url --queue-name "${QUEUE}" --region "${AWS_REGION}" --query QueueUrl --output text 2>/dev/null || echo "")
  
  if [[ -n "${QUEUE_URL}" ]]; then
    echo "  ✓ Fila já existe: ${QUEUE}"
  else
    echo "  Criando: ${QUEUE}"
    QUEUE_URL=$(aws sqs create-queue \
      --queue-name "${QUEUE}" \
      --region "${AWS_REGION}" \
      --query QueueUrl \
      --output text)
    echo "  ✓ Fila criada: ${QUEUE}"
    echo "    URL: ${QUEUE_URL}"
  fi
done

# ---------------------------------------------------------------------------
# 3. Criar ECR repository
# ---------------------------------------------------------------------------
echo ""
echo "--- [ECR] Criando repositório ---"

ECR_REPO="processador-diagramas-reportingservice"
REPO_URI=$(aws ecr describe-repositories \
  --repository-names "${ECR_REPO}" \
  --region "${AWS_REGION}" \
  --query 'repositories[0].repositoryUri' \
  --output text 2>/dev/null || echo "")

if [[ -n "${REPO_URI}" && "${REPO_URI}" != "None" ]]; then
  echo "  ✓ Repositório já existe: ${ECR_REPO}"
  echo "    URI: ${REPO_URI}"
else
  echo "  Criando: ${ECR_REPO}"
  REPO_URI=$(aws ecr create-repository \
    --repository-name "${ECR_REPO}" \
    --region "${AWS_REGION}" \
    --query 'repository.repositoryUri' \
    --output text)
  echo "  ✓ Repositório criado: ${ECR_REPO}"
  echo "    URI: ${REPO_URI}"
fi

# ---------------------------------------------------------------------------
# 4. Atualizar contrato homolog.contract.env
# ---------------------------------------------------------------------------
echo ""
echo "--- [Contrato] Atualizando homolog.contract.env ---"

CONTRACT_FILE=".github/e2e/homolog.contract.env"
if [[ -f "${CONTRACT_FILE}" ]]; then
  sed -i "s|S3_BUCKET_NAME=.*|S3_BUCKET_NAME=processador-diagramas-processingservice-hml-inputs-${AWS_ACCOUNT_ID}|g" "${CONTRACT_FILE}"
  echo "  ✓ S3_BUCKET_NAME atualizado no contrato"
  echo "    Valor: processador-diagramas-processingservice-hml-inputs-${AWS_ACCOUNT_ID}"
fi

# ---------------------------------------------------------------------------
# Resumo
# ---------------------------------------------------------------------------
echo ""
echo "=== Setup Completo ==="
echo ""
echo "Recursos criados:"
echo "  S3 Buckets: ${#S3_BUCKETS[@]}"
echo "  SQS Queues: ${#SQS_QUEUES[@]}"
echo "  ECR Repos: 1"
echo ""
echo "Próximos passos:"
echo "  1. Configurar GitHub Secrets (AWS keys, Docker token)"
echo "  2. Configurar GitHub Vars (AWS_REGION, EKS_CLUSTER_NAME, etc)"
echo "  3. Fazer push para ativar pipeline"
echo ""
