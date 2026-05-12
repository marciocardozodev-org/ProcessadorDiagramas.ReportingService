# AWS Cleanup & Setup — AWS Academy Daily Management

Guia para encerrar o dia com custo baixo no AWS Academy e deixar a retomada pronta para o próximo dia.

## Cenário atual

- RDS compartilhada: `processador-diagramas-pg-hml`
- Database do ReportingService: `processador_diagramas_reporting`
- Usuário de aplicação: `reporting_service`
- Secret Manager do serviço: `/homolog/reportingservice/db-credentials`
- Secret Manager do master compartilhado: `/homolog/shared-rds/master-credentials`
- Cluster EKS compartilhado: `processador-diagramas-shared-eks`

Isso muda a regra do cleanup: recursos recriáveis e deploy podem ser removidos ao fim do dia, mas a RDS compartilhada não deve ser destruída por padrão.

## O que o cleanup atual faz

- Remove filas SQS do ecossistema Processador Diagramas
- Remove buckets S3 do ecossistema Processador Diagramas
- Remove repositórios ECR do ecossistema Processador Diagramas
- Remove recursos do ReportingService no namespace `homolog` do EKS
- Opcionalmente captura um snapshot local com instruções de retomada
- Não remove a RDS compartilhada por padrão

## Final do dia

### 1. Dry-run com snapshot local

```bash
./scripts/aws-cleanup.sh --capture-state
```

Isso grava um snapshot em `artifacts/end-of-day/` com:

- lembretes de retomada
- dados do banco do ReportingService
- nomes dos workflows relevantes

### 2. Cleanup efetivo

```bash
./scripts/aws-cleanup.sh --capture-state --confirm
```

Fluxo esperado:

- SQS removida
- S3 removido
- ECR removido
- deployment/service/job/secret/configmap do ReportingService removidos do namespace `homolog`
- snapshot local salvo para o dia seguinte

### 3. Se existir uma RDS dedicada descartável

Somente quando houver uma instância dedicada e descartável:

```bash
./scripts/aws-cleanup.sh --capture-state --include-rds --confirm
```

No cenário atual, `processador-diagramas-pg-hml` é compartilhada e não deve ser removida por este repositório.

## Início do próximo dia

### 1. Exportar novas credenciais AWS Academy

```bash
export AWS_ACCESS_KEY_ID=...
export AWS_SECRET_ACCESS_KEY=...
export AWS_SESSION_TOKEN=...
export AWS_DEFAULT_REGION=us-east-1
```

### 2. Recriar recursos base

```bash
AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
AWS_ACCOUNT_ID="${AWS_ACCOUNT_ID}" ./scripts/aws-setup-resources.sh
```

### 3. Atualizar GitHub secrets temporários da sessão

Atualize no GitHub:

- `AWS_ACCESS_KEY_ID`
- `AWS_SECRET_ACCESS_KEY`
- `AWS_SESSION_TOKEN`

Garanta também que `HOMOLOG_DB_CONNECTION_STRING` continua configurado.

### 4. Fazer deploy novamente

```bash
git push origin homolog
```

### 5. Validar o serviço

- Rodar a pipeline `ci-cd.yml`
- Rodar o workflow manual `Homolog E2E Isolado — ReportingService`

## Verificações úteis

### Antes do cleanup

```bash
aws sqs list-queues --region us-east-1
aws s3 ls
aws ecr describe-repositories --region us-east-1
kubectl get all -n homolog | grep reporting
```

### Depois do cleanup

```bash
aws sqs list-queues --region us-east-1
aws s3 ls | grep processador-diagramas
aws ecr describe-repositories --region us-east-1 --query 'repositories[?contains(repositoryName, `processador`)]'
kubectl get all -n homolog | grep reporting
```

## Riscos e limites

- O cluster EKS compartilhado continua existindo; este repositório remove o deploy, não o cluster
- A RDS compartilhada continua existindo; este repositório preserva a instância por segurança
- Para zerar quase todo o custo da infra compartilhada, a exclusão do cluster deve acontecer no repositório de infraestrutura
- As credenciais do AWS Academy expiram; o processo de retomada depende de renovar os secrets AWS diariamente

## FAQ

**A RDS é apagada no fim do dia?**

Não por padrão. O banco do ReportingService fica dentro de uma instância compartilhada.

**O que fica salvo para amanhã?**

O comando com `--capture-state` grava um snapshot em `artifacts/end-of-day/`.

**O workflow E2E isolado continua disponível amanhã?**

Sim. Basta recriar recursos base, redeployar e disparar `Homolog E2E Isolado — ReportingService`.

