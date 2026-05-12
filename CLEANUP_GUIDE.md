# 🧹 AWS Cleanup & Setup — AWS Academy Daily Management

Para evitar custos desnecessários em **AWS Academy**, este guia fornece scripts para **limpar recursos no final do dia** e **recriá-los no início do próximo**.

---

## 📋 Recursos que geram custos

- **S3 buckets** (armazenamento)
- **SQS queues** (mensageria além de limite gratuito)
- **RDS instances** (banco de dados)
- **ECR repositories** (privados além de limite gratuito)
- **EKS cluster** (se não estiver no tier gratuito)

---

## 🧹 Cleanup — Ao final do dia

### 1. **Dry-run** (visualizar o que será deletado)

```bash
./scripts/aws-cleanup.sh
```

Output:
```
=== AWS Cleanup — Região: us-east-1 ===

--- [SQS] Listando e limpando filas ---
  [DRY-RUN] Deletar fila SQS: processador-diagramas-processingservice-hml-queue
  [DRY-RUN] Deletar fila SQS: upload-orchestrator-analysis-completed
  ...

Execute novamente com --confirm para deletar recursos:
  ./scripts/aws-cleanup.sh --confirm
```

### 2. **Executar limpeza** (com confirmação)

```bash
./scripts/aws-cleanup.sh --confirm
```

Output:
```
=== AWS Cleanup — Região: us-east-1 ===

--- [SQS] Listando e limpando filas ---
  [EXECUTANDO] Deletar fila SQS: processador-diagramas-processingservice-hml-queue
  ✓ Fila deletada: processador-diagramas-processingservice-hml-queue
  ...

=== Cleanup Completo ===
Todos os recursos foram removidos.
```

---

## 🚀 Setup — Ao início do próximo dia

### 1. **Obter ID da conta AWS**

```bash
aws sts get-caller-identity --query Account --output text
```

### 2. **Criar recursos**

```bash
AWS_ACCOUNT_ID=767398027345 ./scripts/aws-setup-resources.sh
```

Output:
```
=== AWS Setup — Conta: 767398027345, Região: us-east-1 ===

--- [S3] Criando buckets ---
  Criando: processador-diagramas-processingservice-hml-inputs-767398027345
  ✓ Bucket criado: processador-diagramas-processingservice-hml-inputs-767398027345
  ...

--- [SQS] Criando filas ---
  Criando: processador-diagramas-processingservice-hml-queue
  ✓ Fila criada: processador-diagramas-processingservice-hml-queue
    URL: https://queue.amazonaws.com/767398027345/processador-diagramas-processingservice-hml-queue
  ...

=== Setup Completo ===

Recursos criados:
  S3 Buckets: 2
  SQS Queues: 4
  ECR Repos: 1
```

---

## 📅 Daily Workflow (AWS Academy)

### **Final do dia (17h)**

```bash
# Revisar o que será deletado
./scripts/aws-cleanup.sh

# ✅ Após revisar, executar limpeza
./scripts/aws-cleanup.sh --confirm

# ✓ Confirmar via console AWS que recursos foram removidos
# (SQS, S3, ECR não devem aparecer mais)
```

### **Início do próximo dia (9h)**

```bash
# Obter ID da conta
AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)

# Recriar recursos
AWS_ACCOUNT_ID="${AWS_ACCOUNT_ID}" ./scripts/aws-setup-resources.sh

# Verificar que contrato foi atualizado
cat .github/e2e/homolog.contract.env | grep S3_BUCKET_NAME

# Testar E2E local
./scripts/run-e2e-local.sh

# Se tudo OK, fazer push para ativar pipeline
git push origin develop
```

---

## 🔍 Verificação manual (AWS Console)

### **Antes de limpar (dry-run)**

```bash
# Listar SQS
aws sqs list-queues --region us-east-1

# Listar S3
aws s3 ls

# Listar ECR
aws ecr describe-repositories --region us-east-1
```

### **Após limpeza**

```bash
# Confirmar que tudo foi removido
aws sqs list-queues --region us-east-1  # Deve retornar vazio ou apenas filas de outros projetos
aws s3 ls | grep "processador-diagramas"  # Deve estar vazio
aws ecr describe-repositories --region us-east-1 --query 'repositories[?contains(repositoryName, `processador`)]'  # Vazio
```

### **Após setup**

```bash
# Confirmar recursos recriados
aws sqs list-queues --region us-east-1
aws s3 ls | grep "processador-diagramas"
aws ecr describe-repositories --region us-east-1
```

---

## ⚠️ Cuidados

| Ação | Risco | Mitigação |
|---|---|---|
| `aws-cleanup.sh --confirm` sem verificar | Deletar buckets com dados importantes | Sempre rodar **dry-run** primeiro |
| Deixar recursos overnight | Consumir créditos Academy | Rodar cleanup ao final do dia |
| Esquecer de recriar | Testes falharem no dia seguinte | Setup automático atualiza contrato |
| Usar conta errada | Deletar recursos de outro projeto | Confirmar `AWS_ACCOUNT_ID` antes |

---

## 📊 Estimativa de custos (AWS Academy)

- **30 dias de SQS**: ~$0.40
- **30 dias de S3** (100 GB): ~$2.30
- **30 dias de ECR privado**: ~$0 (primeiro 1 repo gratuito)
- **30 dias de EKS**: ~$30 (sem cleanup)

**Com cleanup diário:** ~$0/mês ✅

---

## 🤖 Automação futura

Se quiser automação total (sem intervenção manual):

```bash
#!/bin/bash
# cron job diariamente às 17h (cleanup) e 9h (setup)

# ~/.crontab
0 17 * * * cd /path/to/repo && ./scripts/aws-cleanup.sh --confirm
0 9 * * * cd /path/to/repo && AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text) ./scripts/aws-setup-resources.sh
```

---

## ❓ FAQ

**P: Se deletar um bucket sem limpar objetos?**  
R: O script de cleanup limpa objetos antes de deletar. Se falhar, execute manualmente:
```bash
aws s3 rm s3://bucket-name --recursive
```

**P: Se a fila SQS tiver mensagens?**  
R: O script deleta mesmo com mensagens (comportamento padrão). Se quiser purgar:
```bash
aws sqs purge-queue --queue-url <url>
```

**P: RDS fica aqui?**  
R: Sim, se tiver criado RDS em dias anteriores, o cleanup detecta e remove.

**P: E os logs dos testes?**  
R: Ficar salvos localmente em `artifacts/`. Não são deletados pelo AWS cleanup.

---

**Resumo:** Cleanup no final do dia (5 min), setup no início (3 min). Zero custos adicionais! 🎉

