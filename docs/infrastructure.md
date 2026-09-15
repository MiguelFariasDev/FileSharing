# Infrastructure (AWS) — Etapas 14, 15 e 16

Este documento **prepara e documenta** o deploy em AWS — nenhum recurso descrito aqui foi de fato provisionado. Nenhum comando de `push`/`apply`/`deploy` real foi executado; tudo abaixo é o que seria necessário fazer, com exemplos concretos (comandos, JSON de Task Definition, políticas IAM), para quando o deploy real for decidido. Para a execução local via Docker Compose, ver `docs/development.md`. Para as decisões arquiteturais (diagramas, dois clientes S3, Forwarded Headers), ver `docs/architecture.md`/`docs/deployment.md`. Para os workflows do GitHub Actions que consomem os recursos descritos aqui (ECR, ECS, a IAM Role de OIDC abaixo), ver `docs/ci-cd.md`.

## Abordagem de Infrastructure as Code

Não havia nenhuma ferramenta de IaC no repositório antes desta etapa (`infrastructure/` só continha `docker/`). Introduzir Terraform/CDK/CloudFormation agora, sem um deploy real acontecendo ainda para validar contra, seria "adicionar uma ferramenta por conveniência sem analisar o impacto" — exatamente o que esta etapa pede para evitar. **Decisão: documentação-first.** Este arquivo contém definições concretas (JSON de Task Definition, políticas IAM, tabelas de Security Group) que podem ser copiadas diretamente para o Console AWS, `aws` CLI, ou — quando um deploy real for decidido — servir de base literal para um módulo Terraform/CDK, sem precisar redesenhar nada. Se uma etapa futura decidir por uma ferramenta de IaC, o layout sugerido (caso Terraform) é `infrastructure/aws/{modules,environments/{dev,prod}}/README.md`, mas nenhum arquivo desse tipo foi criado agora.

## AWS ECR — Elastic Container Registry

Nenhum push foi feito. Comandos equivalentes (substituir `<account-id>`/`<region>`):

```bash
# Uma vez por repositório
aws ecr create-repository --repository-name filesharing-api --region <region>
aws ecr create-repository --repository-name filesharing-web --region <region>

# Autenticar o Docker no ECR
aws ecr get-login-password --region <region> \
  | docker login --username AWS --password-stdin <account-id>.dkr.ecr.<region>.amazonaws.com

# Build (a partir da raiz do repositório — ver Dockerfiles)
docker build -f src/FileSharing.Api/Dockerfile -t filesharing-api .
docker build -f src/FileSharing.Web/Dockerfile -t filesharing-web .

# Tag + push
docker tag filesharing-api:latest <account-id>.dkr.ecr.<region>.amazonaws.com/filesharing-api:latest
docker tag filesharing-web:latest <account-id>.dkr.ecr.<region>.amazonaws.com/filesharing-web:latest
docker push <account-id>.dkr.ecr.<region>.amazonaws.com/filesharing-api:latest
docker push <account-id>.dkr.ecr.<region>.amazonaws.com/filesharing-web:latest
```

Nenhuma credencial AWS fica armazenada no projeto — `aws ecr get-login-password` usa a sessão/credenciais já configuradas localmente (`aws configure`/SSO), nunca um segredo commitado.

## AWS S3 (produção)

- **Bucket privado**, Block Public Access habilitado nos quatro controles (mesma postura já usada pelo LocalStack em dev — `infrastructure/docker/localstack-init/01-create-bucket.sh`).
- **Nenhuma credencial de acesso (Access Key/Secret Key) no container.** Em produção, `AWS:AccessKey`/`AWS:SecretKey` devem ficar **vazios** — `StorageExtensions.BuildS3Client` já cai automaticamente na cadeia padrão de credenciais do SDK (`new AmazonS3Client(s3Config)`, sem chaves explícitas) quando esses dois valores não são configurados, que em uma task ECS resolve para as credenciais temporárias do **Task Role** via o endpoint de credenciais do container — nenhuma mudança de código necessária para isso, já era o comportamento correto desde antes desta etapa.
- **`AWS:PublicServiceURL` não deve ser configurado em produção** — o S3 real já tem um único endpoint público por região; a separação de dois endpoints é exclusiva do cenário LocalStack containerizado (ver `docs/architecture.md`).
- **Presigned PUT/GET, nunca ACL pública** — inalterado desde a Etapa 3/5; `CreatePresignedUploadUrlAsync`/`CreatePresignedDownloadUrlAsync` continuam sendo a única forma de acesso a um objeto.
- **Lifecycle/expiração:** os objetos já são explicitamente apagados pelo job `expired-file-cleanup` (Hangfire, Etapa 6) até 15 minutos (configurável) depois de `ExpiresAtUtc`. Como camada adicional (defesa em profundidade, não substitui o job), uma S3 Lifecycle Rule expirando objetos no prefixo do bucket após, por exemplo, 2 dias, cobriria o caso em que o job falhar silenciosamente por um longo período — sugestão documentada, não configurada por esta etapa (não há infraestrutura real para aplicá-la).
- **IAM (Task Role da Api) — política mínima necessária:**

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["s3:PutObject", "s3:GetObject", "s3:DeleteObject", "s3:HeadObject"],
      "Resource": "arn:aws:s3:::filesharing-prod/*"
    }
  ]
}
```

  Nunca `s3:*`, nunca `Resource: "*"` — a Api só precisa agir sobre objetos dentro do próprio bucket, nunca listar/gerenciar o bucket em si (`s3:ListBucket`/`s3:PutBucketPolicy` não são necessários em tempo de execução).

## AWS RDS PostgreSQL

- **Versão:** PostgreSQL 16.x (mesma major version usada localmente — `postgres:16-alpine`), compatível com o provider Npgsql/EF Core já em uso.
- **Não publicamente acessível** (`PubliclyAccessible: false`) — só alcançável de dentro da VPC, e só pelo Security Group do ECS (ver seção Security Groups abaixo).
- **Credenciais via Secrets Manager**, nunca em variável de ambiente em texto puro na Task Definition (ver seção Secrets Manager). A Task Definition referencia o segredo pelo ARN; o valor real nunca aparece em nenhum lugar versionado ou visível no Console de Task Definition.
- **Connection string:** `ConnectionStrings__Postgres` (env var, note o `__` — convenção do ASP.NET Core para seções aninhadas), formato `Host=<endpoint-rds>;Port=5432;Database=filesharing;Username=<user>;Password=<secret>`. Adicionar `SSL Mode=Require;Trust Server Certificate=true` (ou o certificado real da CA da AWS) quando TLS for exigido pela instância RDS — o parâmetro é aceito nativamente pelo Npgsql, nenhuma mudança de código necessária.
- **Backup automático** (RDS automated backups) recomendado, mesmo em um ambiente MVP — é uma configuração da instância, não do código da aplicação.
- Migrações continuam manuais (`dotnet ef database update` contra o endpoint do RDS, a partir de uma máquina com acesso à VPC — ex. um bastion host ou uma execução única de task ECS — nunca expondo o RDS à Internet só para rodar uma migração).

## AWS ECS Fargate

Dois serviços independentes (`filesharing-api`, `filesharing-web`), cada um com sua própria Task Definition. Valores de CPU/memória abaixo são um ponto de partida razoável para um MVP/portfólio — **não são definitivos**, ajustáveis conforme uso real observado (CloudWatch Metrics de CPU/memória da task, uma vez em produção). A Etapa 16 (`docs/performance.md`) mediu o container `filesharing-api` local usando **~70–120MiB** de memória sob carga sustentada (20 VUs por 5 minutos, sem tendência de crescimento) — um dado real, ainda que de um ambiente Docker Compose local, não do ECS Fargate real; os valores de CPU/memória da task abaixo continuam uma estimativa de partida, não derivada diretamente dessa medição.

### Task Definition — `filesharing-api` (exemplo)

```json
{
  "family": "filesharing-api",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "cpu": "512",
  "memory": "1024",
  "executionRoleArn": "arn:aws:iam::<account-id>:role/filesharing-ecs-execution-role",
  "taskRoleArn": "arn:aws:iam::<account-id>:role/filesharing-api-task-role",
  "containerDefinitions": [
    {
      "name": "api",
      "image": "<account-id>.dkr.ecr.<region>.amazonaws.com/filesharing-api:latest",
      "portMappings": [{ "containerPort": 8080, "protocol": "tcp" }],
      "environment": [
        { "name": "ASPNETCORE_ENVIRONMENT", "value": "Production" },
        { "name": "AWS__Region", "value": "<region>" },
        { "name": "FileStorage__BucketName", "value": "filesharing-prod" },
        { "name": "FileStorage__Region", "value": "<region>" },
        { "name": "PasswordReset__WebResetUrlBase", "value": "https://app.example.com/reset-password" }
      ],
      "secrets": [
        { "name": "ConnectionStrings__Postgres", "valueFrom": "arn:aws:secretsmanager:<region>:<account-id>:secret:filesharing/postgres-connection-string" },
        { "name": "Jwt__SecretKey", "valueFrom": "arn:aws:secretsmanager:<region>:<account-id>:secret:filesharing/jwt-secret-key" }
      ],
      "healthCheck": {
        "command": ["CMD-SHELL", "curl --fail http://localhost:8080/health/live || exit 1"],
        "interval": 30,
        "timeout": 5,
        "retries": 3,
        "startPeriod": 20
      },
      "logConfiguration": {
        "logDriver": "awslogs",
        "options": {
          "awslogs-group": "/ecs/filesharing-api",
          "awslogs-region": "<region>",
          "awslogs-stream-prefix": "api"
        }
      }
    }
  ]
}
```

Note: `AWS__AccessKey`/`AWS__SecretKey` **não aparecem** — deliberadamente ausentes, para que o SDK use o Task Role (ver seção S3 acima). `AWS__ServiceURL`/`AWS__PublicServiceURL` também ficam ausentes — sem eles, o S3 real (endpoint padrão) é usado, exatamente como em qualquer ambiente que não seja o Docker Compose local.

### Task Definition — `filesharing-web` (exemplo, resumido)

Mesmo padrão: `cpu: 256`, `memory: 512` (o Web é bem mais leve — Blazor Server sem acesso a banco/S3 direto), `Api__BaseUrl` apontando para o DNS interno do serviço `filesharing-api` via Service Discovery/Cloud Map **ou**, mais simples para um MVP, para a URL pública do próprio ALB roteada até a Api (`https://app.example.com/api` com regra de path no ALB) — a escolha exata depende de como as regras de roteamento do ALB forem desenhadas; ambas são válidas e não exigem mudança de código no Web (`Api:BaseUrl` já é uma configuração externa desde a Etapa 3).

### Cluster e serviço

- Um cluster ECS Fargate (`filesharing-cluster`) hospedando os dois serviços.
- `desiredCount: 1` para ambos inicialmente (MVP) — ver a nota sobre SignalR sem backplane em `docs/deployment.md` antes de aumentar o da Api.
- Cada serviço registrado em um Target Group próprio do ALB (ver próxima seção).

## Application Load Balancer

- **Um ALB**, dois Target Groups (`tg-api`, `tg-web`), roteamento por path (`/api/*`, `/hubs/*` → `tg-api`; tudo mais → `tg-web`) ou por subdomínio (`api.example.com` → `tg-api`, `app.example.com` → `tg-web`) — decisão de produto, não técnica; ambas funcionam sem mudança de código.
- **Health check de cada Target Group:** `/health/live` (nunca `/health/ready` — ver `docs/deployment.md`), porta `8080`, protocolo HTTP (o ALB fala HTTP simples com o ECS; TLS termina no próprio ALB).
- **Listener HTTPS (443)** com certificado ACM (ver próxima seção); **listener HTTP (80) redirecionando para HTTPS**, nunca servindo tráfego em texto puro.
- **WebSocket/SignalR:** o ALB suporta upgrade de conexão HTTP→WebSocket nativamente, sem configuração especial — só é preciso garantir que o **Idle Timeout** do listener (padrão 60s) seja alto o suficiente para não derrubar conexões SignalR ociosas (aumentar para, por ex., 300s no Target Group da Api é a recomendação usual para apps com SignalR/WebSocket atrás de um ALB).
- **Cabeçalhos encaminhados:** o ALB já injeta `X-Forwarded-For`/`X-Forwarded-Proto`/`X-Forwarded-Port` automaticamente em toda requisição — é exatamente isso que o `UseForwardedHeaders` (Etapa 14, ver `docs/deployment.md`) foi adicionado para consumir corretamente.
- **Deploy gradual:** o comportamento padrão do ECS (rolling update, aguardando o health check do Target Group antes de desregistrar a task antiga) já é suficiente para um deploy sem downtime perceptível num serviço com `desiredCount: 1` mínimo de 1 task saudável durante a troca — nenhuma configuração adicional de deployment (blue/green, canary) foi definida nesta etapa.

## HTTPS / AWS Certificate Manager (ACM)

- Certificado emitido pelo ACM para o domínio usado (`app.example.com`/`api.example.com`), validado via DNS (Route 53 ou o provedor de DNS em uso), anexado ao listener HTTPS do ALB.
- **Nenhum certificado dentro da aplicação** — nem a Api nem o Web têm ou devem ter um certificado TLS próprio; toda a terminação TLS acontece no ALB.
- **Nenhum HTTP público em produção** — o listener 80 do ALB só existe para redirecionar a 443, nunca para servir a aplicação diretamente.
- Localmente (Docker Compose), a aplicação continua em HTTP simples — isso é aceitável e não foi alterado, conforme pedido explicitamente por esta etapa.

## AWS Secrets Manager

Segredos a externalizar (nenhum tem valor real neste documento nem em qualquer arquivo versionado):

| Segredo | Nome sugerido | Consumido por |
|---|---|---|
| Senha do RDS PostgreSQL / connection string completa | `filesharing/postgres-connection-string` | Api (`ConnectionStrings__Postgres`) |
| Chave de assinatura do JWT | `filesharing/jwt-secret-key` | Api (`Jwt__SecretKey`) |

Ambos são referenciados na Task Definition via o bloco `secrets` (ver exemplo JSON acima) — o ECS injeta o valor real como variável de ambiente **no momento em que o container inicia**, buscando do Secrets Manager usando as permissões do **Execution Role** (nunca do Task Role — ver seção IAM). O valor nunca fica gravado na definição da task, na imagem, ou em qualquer lugar versionado.

## CloudWatch

- **Logs:** driver `awslogs` na Task Definition (ver exemplo JSON acima) — a aplicação já escreve para `stdout`/`stderr` de forma estruturada (Serilog/console formatter, desde etapas anteriores); nenhuma mudança de código é necessária, só a configuração do driver de log na task.
- **O que nunca deve aparecer em log, revisado e válido também em produção** (ver `docs/security.md`, seção "Observability & Diagnostics" e "O que nunca aparece em log"): JWT, header `Authorization`, senha, `AccessToken`/`AccessTokenHash`, presigned URLs (upload e download), `StorageKey`, connection string, credenciais AWS, cookies, conteúdo de arquivo. Nada nesta etapa altera esse conjunto de logs — a mudança de destino (console local → CloudWatch Logs via `awslogs`) não muda o que é escrito, só para onde vai.
- **Métricas/Traces:** não conectados a nenhum backend nesta etapa (decisão já tomada e documentada na Etapa 12 — ver `docs/security.md`) — `AppMetrics` (`System.Diagnostics.Metrics`) já expõe um `Meter` (`"FileSharing.Application"`) pronto para ser consumido por um exportador CloudWatch EMF/OpenTelemetry no futuro, sem alterar código existente.

## IAM

Dois roles distintos por serviço ECS — nunca um único role acumulando as duas responsabilidades, e nunca `AdministratorAccess`:

### Execution Role (`filesharing-ecs-execution-role`)

Usado pelo próprio agente do ECS para preparar a task — puxar a imagem do ECR, escrever logs, buscar segredos do Secrets Manager. **Nunca** usado pelo código da aplicação em tempo de execução.

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["ecr:GetAuthorizationToken", "ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"],
      "Resource": "*"
    },
    {
      "Effect": "Allow",
      "Action": ["logs:CreateLogStream", "logs:PutLogEvents"],
      "Resource": "arn:aws:logs:<region>:<account-id>:log-group:/ecs/filesharing-*"
    },
    {
      "Effect": "Allow",
      "Action": ["secretsmanager:GetSecretValue"],
      "Resource": [
        "arn:aws:secretsmanager:<region>:<account-id>:secret:filesharing/postgres-connection-string-*",
        "arn:aws:secretsmanager:<region>:<account-id>:secret:filesharing/jwt-secret-key-*"
      ]
    }
  ]
}
```

(`ecr:GetAuthorizationToken` exige `Resource: "*"` — é uma limitação documentada da própria API do ECR, não uma concessão ampla real de permissões sobre recursos.)

### Task Role (`filesharing-api-task-role`)

Usado pelo **código da aplicação** em tempo de execução (é o que o SDK da AWS lê via a cadeia padrão de credenciais). Só a política de S3 já mostrada acima (`s3:PutObject`/`GetObject`/`DeleteObject`/`HeadObject` restrito ao bucket `filesharing-prod`). O serviço Web não precisa de um Task Role com nenhuma permissão de API AWS (não chama S3/RDS diretamente) — só do Execution Role padrão para pull de imagem/logs.

### GitHub Actions OIDC Role (`github-actions-filesharing-deploy`) — Etapa 15

Um terceiro role, distinto dos dois acima — usado pelo **workflow de CD** (GitHub Actions), nunca pela aplicação em execução. Autenticação via OpenID Connect (nenhuma AWS Access Key/Secret Key armazenada no GitHub) — trust policy, permission policy completa e o passo a passo de configuração estão em `docs/ci-cd.md` (seção "GitHub OIDC → AWS"). Resumo da permission policy: `ecr:*` (login/push, escopado aos dois repositórios deste projeto), `ecs:DescribeTaskDefinition`/`RegisterTaskDefinition`/`UpdateService`/`RunTask`/`DescribeTasks`/`ListTaskDefinitions` (escopados ao cluster `filesharing-cluster`), e `iam:PassRole` restrito exatamente aos dois ARNs acima (`filesharing-ecs-execution-role`, `filesharing-api-task-role`) — nunca `iam:PassRole` sem `Resource` explícito, o que permitiria passar qualquer role da conta.

## Security Groups

| Origem | Destino | Porta | Motivo |
|---|---|---|---|
| Internet (`0.0.0.0/0`) | ALB | 443 | Tráfego HTTPS público |
| Internet (`0.0.0.0/0`) | ALB | 80 | Só para redirect 301 → 443, nunca serve conteúdo |
| ALB (seu próprio SG) | ECS (Api e Web) | 8080 | Tráfego roteado do ALB para os containers |
| ECS (Api) | RDS | 5432 | Só a Api fala com o banco — o Web nunca acessa o RDS diretamente |
| ECS (Api) | Internet (via NAT Gateway, se em subnet privada) | 443 | Chamadas de API do SDK AWS ao S3/Secrets Manager (endpoints públicos da AWS, ou VPC Endpoints para evitar NAT) |
| — | RDS | — | **Nunca aceita tráfego da Internet** — Security Group do RDS só lista o SG do ECS como origem permitida |
| — | Postgres/portas internas | — | **Nunca expostas publicamente** — nem RDS nem qualquer porta administrativa têm regra de entrada `0.0.0.0/0` |

## Custo (MVP / portfólio)

Estimativa aproximada, região `us-east-1`, uso baixo/contínuo (não é uma cotação, varia por região/uso real):

| Recurso | Custo aproximado (mensal) | Pode ser desligado/simplificado em dev? |
|---|---|---|
| ECS Fargate (2 tasks pequenas, ~512+256 vCPU/mem) | ~US$15–25 | Sim — parar os serviços (`desiredCount: 0`) fora do horário de demonstração |
| RDS PostgreSQL (instância `db.t4g.micro`) | ~US$12–15 | Sim — usar o Postgres em Docker Compose para qualquer avaliação que não exija AWS real |
| Application Load Balancer | ~US$16–20 (cobrança por hora + LCU) | Não facilmente — é por hora de existência do ALB, independente de tráfego; considerar deletar entre demonstrações se custo for uma restrição real |
| S3 | Centavos (armazenamento + requests, arquivos de curta duração) | Já é praticamente gratuito neste padrão de uso (24h de retenção) |
| Secrets Manager | ~US$0,40/segredo/mês | Não vale a pena remover — custo desprezível |
| CloudWatch Logs | Centavos a poucos dólares (volume baixo) | Ajustar retenção do Log Group (ex. 7–14 dias) para reduzir custo de armazenamento |
| ECR | Centavos (poucas imagens, poucos GB) | Não |
| Data transfer (ALB↔Internet, NAT Gateway se usado) | Variável, tende a ser pequeno neste volume | Evitar NAT Gateway usando subnets públicas para o ECS (aceitável para um MVP; trade-off de isolamento de rede documentado aqui, não implementado) |

**Maior custo fixo e menos "desligável": o ALB.** Para um portfólio que não precisa ficar no ar 24/7, a alternativa de menor custo é derrubar o ambiente (`ecs update-service --desired-count 0`, ou deletar o stack inteiro) entre demonstrações, e recriar quando necessário — nenhum dado de aplicação é perdido (RDS/S3 persistem independentemente do ECS/ALB estarem no ar), exceto o próprio ALB e Target Groups, que precisariam ser recriados (rápido, mas não instantâneo).

Nenhuma substituição de componente (ex. trocar RDS por Postgres num único container EC2 "para economizar") foi feita só por causa de custo — isso mudaria a arquitetura (perderia backup automático, Multi-AZ opcional, isolamento de rede do RDS) sem que o enunciado desta etapa pedisse essa troca.

## Mobile — conectividade (Android e iOS)

Não containerizado (correto — é um app nativo, não um serviço de longa duração). O único ajuste necessário para apontar o Mobile para qualquer ambiente (local, AWS) é `src/FileSharing.Mobile/Resources/Raw/appsettings.json` (`BaseUrl`/`HubUrl`, ver `docs/mobile.md`):

- **Contra o Docker Compose local:** ver a tabela de endereços em `docs/development.md` (`10.0.2.2` no emulador Android, IP da máquina host em dispositivo físico, `localhost` no simulador iOS).
- **Contra AWS (produção):** `BaseUrl = https://api.example.com`, `HubUrl` derivado (`/hubs/notifications`) — HTTPS obrigatório (o ALB nunca serve HTTP em produção), sem nenhuma configuração de emulador/host especial, já que o domínio é público e igualmente alcançável de qualquer rede.
- Upload direto ao S3 (Etapa 3) continua idêntico — o Mobile nunca fala diretamente com o S3 exceto para o PUT/GET da presigned URL, que em produção já aponta para o endpoint real do S3 (sem o ajuste de `PublicServiceURL` necessário só em dev).
- **Nada disso foi alterado nesta etapa:** nenhum App Link/Universal Link foi adicionado, nenhuma revogação de JWT foi implementada — ambos permanecem deliberadamente fora de escopo, como em etapas anteriores.

## Checklist de auditoria — o que já estava pronto vs. o que esta etapa ajustou

**Já estava pronto (verificado, não alterado):**
- Toda configuração já era externalizada via `IConfiguration`/variáveis de ambiente — nenhum `appsettings.json` versionado carrega segredo real.
- `IFileStorageService`/S3 já usava a cadeia padrão de credenciais quando `AccessKey`/`SecretKey` não configurados — pronto para Task Role sem mudança.
- Health checks (`/health/live`, `/health/ready`) já existiam na Api com a semântica correta para um orquestrador.
- Hangfire já usa PostgreSQL como storage compartilhado com `DisableConcurrentExecution`, seguro para múltiplas tasks (para o próprio job de limpeza — não para SignalR, ver limitação documentada).
- Logs estruturados (Serilog/console) já prontos para redirecionamento via `awslogs`, sem mudança de código.

**Ajustado nesta etapa (lacunas reais encontradas na auditoria):**
- `UseForwardedHeaders` ausente em Api e Web — corrigido (pré-requisito para operar corretamente atrás do ALB).
- Presigned URLs não tinham como apontar para um host diferente do endpoint interno do S3/LocalStack quando containerizado — corrigido com o cliente de presign dedicado (`AWS:PublicServiceURL`).
- `FileSharing.Web` não tinha nenhum endpoint de health check — adicionado `/health/live`.
- Nenhum Dockerfile de produção existia para Api/Web — criados (multi-stage, não-root, sem segredo).
- `docker-compose.yml` tinha credenciais do Postgres hardcoded — parametrizado via `.env`.
- **Etapa 16**: `RateLimiting:Auth:PermitLimit`/`RateLimiting:PasswordReset:PermitLimit`/`ExpirationCleanup:IntervalMinutes`/`ExpirationCleanup:BatchSize` já eram configuráveis via `IConfiguration` desde etapas anteriores, mas nunca estavam conectados ao `docker-compose.yml` — conectados agora (com os mesmos valores padrão já hardcoded no código, então nada muda por padrão), necessário para os benchmarks de capacidade/volume documentados em `docs/performance.md`. Gargalo real encontrado e corrigido em `ExpiredFileCleanupJob` (change tracker do EF Core crescendo a cada item de um lote grande) — ver `docs/performance.md`, seção Hangfire.
