#!/usr/bin/env bash
# =============================================================================
# Runner local E2E (microservico apenas) — ProcessadorDiagramas.ReportingService
# =============================================================================
# Uso:
#   ./scripts/run-e2e-local.sh
#
# Executa o smoke E2E local do proprio servico via docker compose,
# sem dependencia de AWS, SQS/SNS ou comunicacao com outros microservicos.
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

exec "${SCRIPT_DIR}/test-docker-compose-flow.sh"
