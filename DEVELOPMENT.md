# Guia de Desenvolvimento Local

## Objetivo da etapa atual

Validar a base do microserviço com domínio, aplicação e persistência em EF Core, agora alinhada ao consumo assíncrono do ProcessingService via SQS e mantendo o fallback HTTP legado apenas para compatibilidade.

## Loop local

```bash
dotnet build ProcessadorDiagramas.ReportingService.sln
dotnet test ProcessadorDiagramas.ReportingService.sln
```

## Subir banco e aplicar migrations

```bash
docker compose up postgres -d
dotnet ef database update \
    --project src/ProcessadorDiagramas.ReportingService.Infrastructure/ProcessadorDiagramas.ReportingService.Infrastructure.csproj \
    --startup-project src/ProcessadorDiagramas.ReportingService.API/ProcessadorDiagramas.ReportingService.API.csproj
```

## Smoke test com Docker Compose

```bash
docker compose up --build
curl http://localhost:5081/health
./scripts/test-docker-compose-flow.sh
```

O smoke test valida:

- PostgreSQL local (porta 5435)
- Migrations via efbundle
- Health check com banco (`/health` e `/ready`)
- Swagger disponível (`/swagger`)
- Endpoints internos existem e respondem (`/internal/reports/{id}` e `/internal/reports/{id}/generate`)

## Deploy rápido no Minikube

```bash
./scripts/minikube/build-local-image.sh
IMAGE_TAG=local ./scripts/minikube/deploy.sh
```

Após o deploy, expor a porta localmente:

```bash
kubectl port-forward -n reporting-local svc/processador-diagramas-reportingservice 5081:80
curl http://localhost:5081/health
curl http://localhost:5081/swagger
```

## Variáveis de ambiente necessárias

| Variável | Descrição | Padrão local |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | String de conexão com o banco de relatórios | `Host=localhost;Port=5435;...` |
| `ProcessingService__BaseUrl` | URL base do ProcessingService | `http://localhost:5080` |
| `ProcessingService__TimeoutSeconds` | Timeout do client HTTP | `30` |

## Banco de dados próprio

Este serviço usa banco PostgreSQL independente:

- **Nome do banco:** `processador_diagramas_reporting`
- **Porta local:** `5435` (não conflita com o ProcessingService em `5434`)
- **Tabela principal:** `AnalysisReports`

## Endpoints internos

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/internal/reports/{analysisProcessId}` | Retorna ou gera o relatório sob demanda |
| `POST` | `/internal/reports/{analysisProcessId}/generate` | Força regeneração do relatório |
| `GET` | `/health` | Health check geral + banco |
| `GET` | `/ready` | Readiness probe (banco obrigatório) |
| `GET` | `/swagger` | Documentação Swagger (Development) |

## Direção das próximas etapas

- Testes do `RegenerateAnalysisReportCommandHandler` e `ReportsController`
- Retry policy no `ProcessingServiceClient` com Polly
- Testes de repositório com EF InMemory
- Evolução do contrato compartilhado do evento `AnalysisProcessingCompletedV2`
