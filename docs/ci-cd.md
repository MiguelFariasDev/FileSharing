# CI/CD — Etapa 15

Documenta a automação de build, testes, imagens Docker e deployment AWS implementada nesta etapa, sobre a arquitetura já existente (Etapa 14 — Docker + preparação para AWS). **Nenhum recurso AWS foi criado ou modificado por esta etapa** — os workflows de deploy (`backend.yml`/`web.yml`, job `deploy`) checam explicitamente se a configuração AWS existe e se auto-desabilitam (skip, não falha) quando não existe, exatamente como hoje (nenhum ECR/ECS/RDS real provisionado — ver `docs/infrastructure.md`). Quando o deploy real for decidido, a seção "Habilitando o deploy real" abaixo é o roteiro.

## Estrutura dos workflows

Preservada a estrutura já existente no repositório (`.github/workflows/backend.yml`, `web.yml`, `mobile.yml`, `infrastructure.yml` — criados como arquivos vazios em etapas anteriores) em vez de introduzir uma divisão diferente (`ci.yml`/`docker.yml`/`deploy.yml`) — dividir por **componente** mantém o pipeline completo de cada serviço (build→test→docker→deploy) em um único arquivo, mais fácil de acompanhar do que espalhar por várias camadas horizontais.

| Workflow | Escopo | O que faz |
|---|---|---|
| `backend.yml` | Domain, Application, Infrastructure, Api | CI (restore/build/format/audit/testes/docker build) em todo PR e push a `main`; CD (ECR + ECS + migração + health check + smoke test) só em push a `main` |
| `web.yml` | Web (Blazor Server) | Mesmo padrão do backend, escopado ao projeto Web |
| `mobile.yml` | Mobile, Mobile.Core | Testes da lógica compartilhada, build Android (Ubuntu) e build iOS (macOS) — sem deploy |
| `infrastructure.yml` | Docker Compose (Api+Web+Postgres+LocalStack) | Build das duas imagens + sobe a stack completa + roda um E2E real (`infrastructure/docker/e2e-smoke-test.sh`) — valida a composição dos containers, não a lógica de negócio (já coberta pelos outros workflows) |

Cada workflow escopado ao seu próprio projeto (nunca `dotnet build FileSharing.slnx` inteira) por um motivo concreto encontrado na auditoria desta etapa: `FileSharing.Mobile` tem `net10.0-android` (e, fora do Linux, `net10.0-ios`/`net10.0-maccatalyst`) como `TargetFrameworks` — restaurar/compilar a `.slnx` completa em um runner sem o workload MAUI instalado falha. `backend.yml`/`web.yml`/`infrastructure.yml` rodam em `ubuntu-latest` sem esse workload (mais rápido, sem necessidade); só `mobile.yml` o instala, e só nos jobs que realmente compilam para uma plataforma MAUI.

## Triggers

Todos os quatro workflows: `pull_request` (para `main`) e `push` (para `main`). Sem filtro de `paths` — decisão deliberada de simplicidade (ver `docs/ci-cd.md` mesma seção mais abaixo, "Decisões"): o tamanho atual do projeto não justifica a complexidade de manter path filters sincronizados com a estrutura de pastas, e rodar os quatro workflows sempre garante que uma mudança em um projeto nunca escapa despercebida de uma verificação em outro (ex.: uma mudança em `Directory.Packages.props` afeta todos).

- **CI (jobs `validate`/`test`/`docker-build`)**: roda em `pull_request` e em `push` para `main` — o princípio de "falhar cedo" (`restore → build → tests → format → security → docker build`) é o mesmo dos dois triggers, só o job `deploy` depende de qual eram.
- **CD (job `deploy`, só em `backend.yml`/`web.yml`)**: `if: github.ref == 'refs/heads/main' && github.event_name == 'push'` — nunca roda em Pull Request, inclusive de forks (branch protection deve exigir que os workflows de CI passem antes do merge — ver "Branch strategy" abaixo).

## Branch strategy

Preservado o fluxo já existente no repositório: um único branch de longa duração, `main`. Nenhum Git Flow complexo foi introduzido — branches de feature (`feature/*`, ou qualquer nome) abrem Pull Request para `main`; merge em `main` dispara o CD. Não existe hoje um branch/ambiente `staging` real (nenhuma infraestrutura AWS de staging distinta foi criada em nenhuma etapa) — só um `GitHub Environment` (`production`) é usado, deliberadamente (ver "GitHub Environments" abaixo).

**Configuração recomendada de branch protection para `main`** (Settings → Branches → `main`, ação manual — não é algo que um workflow YAML possa configurar sozinho):
- Exigir Pull Request antes de merge.
- Exigir que os status checks `Backend CI/CD / validate`, `Backend CI/CD / unit-and-api-tests`, `Backend CI/CD / integration-tests`, `Web CI/CD / validate`, `Web CI/CD / test`, `Mobile CI / test`, `Infrastructure — Docker Compose E2E / compose-e2e` passem antes de permitir o merge.
- Não permitir push direto a `main` sem PR (isso é o que realmente impede um deploy sem CI — o trigger `push` do CD por si só não valida nada, só o encadeamento `needs:` dentro do mesmo run; um push direto a `main`, se permitido pelo repositório, ainda rodaria a cadeia completa de `validate`→testes→`docker-build`→`deploy` no mesmo workflow, mas branch protection é o que impede alguém de *pular* a revisão do PR).

## GitHub Environments

Só `production` foi configurado — nenhum `development`/`staging` foi criado, porque não existe hoje nenhuma infraestrutura AWS real para eles apontarem (adicionar Environments sem um destino real seria complexidade decorativa). O ambiente local (Docker Compose) já cumpre o papel de "development" sem precisar de um GitHub Environment para isso.

**Como configurar o Environment `production`** (Settings → Environments → New environment → `production`):
1. **Proteção**: marcar "Required reviewers" (aprovação manual antes do job `deploy` rodar) — recomendado assim que o deploy real for habilitado, para nunca implantar em produção sem uma revisão humana explícita, mesmo depois do CI já ter passado.
2. **Variáveis do ambiente** (Settings → Environments → `production` → Environment variables — não secrets, esses valores não são sensíveis):
   - `AWS_REGION` — região AWS (ex. `us-east-1`).
   - `AWS_ROLE_TO_ASSUME` — ARN da IAM Role assumida via OIDC (ver seção abaixo). **Enquanto esta variável não existir, o job `deploy` se auto-desabilita** (skip explícito, ver `backend.yml`/`web.yml`, step "Skip if AWS deployment is not configured yet") — é assim que este repositório funciona hoje, sem nenhum recurso AWS provisionado.
   - `ECS_NETWORK_CONFIGURATION` — o JSON de `awsvpcConfiguration` (subnets/security groups) usado pela task one-off de migração (`aws ecs run-task --network-configuration`).
   - `API_PUBLIC_HOSTNAME` / `WEB_PUBLIC_HOSTNAME` — hostname público (atrás do ALB/ACM) usado pelo health check/smoke test pós-deploy.
3. **Nenhum AWS Access Key/Secret Key como secret** — a autenticação é via OIDC (ver abaixo), não credenciais de longa duração.

## GitHub OIDC → AWS (sem AWS keys permanentes)

Nenhuma `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY` é armazenada no GitHub. Os workflows declaram:

```yaml
permissions:
  id-token: write
  contents: read
```

e usam `aws-actions/configure-aws-credentials@v4` com `role-to-assume` (nunca `aws-access-key-id`/`aws-secret-access-key`) — o runner troca o token OIDC do próprio GitHub Actions por credenciais temporárias da AWS STS.

### IAM: configuração necessária (não provisionada por esta etapa)

1. **Identity Provider OIDC** no IAM, uma vez por conta AWS:
   - URL: `https://token.actions.githubusercontent.com`
   - Audience: `sts.amazonaws.com`

2. **IAM Role** (ex. `github-actions-filesharing-deploy`) com uma **trust policy restrita ao repositório e ao branch `main`** — nunca "qualquer repositório", nunca "qualquer branch":

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::<account-id>:oidc-provider/token.actions.githubusercontent.com"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "token.actions.githubusercontent.com:aud": "sts.amazonaws.com"
        },
        "StringLike": {
          "token.actions.githubusercontent.com:sub": "repo:MiguelFariasDev/FileSharing:ref:refs/heads/main"
        }
      }
    }
  ]
}
```

   A condição `sub` restringe exatamente a `repo:MiguelFariasDev/FileSharing:ref:refs/heads/main` — nem um fork, nem um PR de fora, nem outro branch consegue assumir esta role (o `sub` de um PR normal é `repo:<org>/<repo>:pull_request`, que não casa com esse padrão). Se um Environment do GitHub for usado como camada adicional de restrição (recomendado), o `sub` correspondente é `repo:MiguelFariasDev/FileSharing:environment:production`.

3. **Permission policy da role** — least privilege, nunca `AdministratorAccess`. Precisa cobrir exatamente o que os workflows fazem: login/push no ECR, `ecs:DescribeTaskDefinition`/`RegisterTaskDefinition`/`UpdateService`/`RunTask`/`DescribeTasks` (escopados ao cluster/família de tasks deste projeto), e `iam:PassRole` para as duas roles de execução/task do ECS (ver `docs/infrastructure.md`, seção IAM, para os ARNs exatos). Um exemplo completo de política está em `docs/infrastructure.md`.

### Por que isso é seguro mesmo com `id-token: write`

- A trust policy acima é o que realmente impede abuso — mesmo que outro workflow neste mesmo repositório peça `id-token: write`, só uma execução vinda do branch `main` (ou do Environment `production`) recebe um token cujo `sub` a role aceita.
- Pull Requests de forks nunca recebem os secrets/variáveis do Environment `production` por padrão do próprio GitHub (Environments protegidos não expõem suas variáveis/secrets a workflows disparados por PRs de fork) — reforça, não substitui, a restrição da trust policy.
- Least privilege na permission policy limita o dano mesmo se a role fosse assumida indevidamente por algum outro caminho não prevbisto.

## Amazon ECR

Login via `aws-actions/amazon-ecr-login@v2` (usa as credenciais OIDC já configuradas, nunca `docker login` manual com uma senha). Repositórios: `filesharing-api`, `filesharing-web` — mesmos nomes já documentados em `docs/infrastructure.md`.

### Tagging

Nunca só `latest`. Cada imagem recebe duas tags no push:

- `<ecr-registry>/filesharing-api:<commit-sha>` — a tag que o ECS efetivamente usa no deploy (`github.sha`, sempre o SHA completo de 40 caracteres). Permite saber exatamente qual commit está rodando em produção a qualquer momento (`aws ecs describe-services`/`describe-task-definition` mostra a URI da imagem, que contém o SHA).
- `<ecr-registry>/filesharing-api:latest` — mantida só como conveniência/referência humana (ex. `docker pull ... :latest` para inspecionar rapidamente a build mais recente) — **nunca** é o que a Task Definition referencia; o deploy sempre aponta para a tag de SHA explícita.

## AWS ECS — deployment

Fluxo (idêntico em `backend.yml`/`web.yml`, cada um para seu próprio serviço):

```
Build & push imagem (tag = commit SHA)
  ↓
aws ecs describe-task-definition   (baixa a definição JÁ EXISTENTE no ECS)
  ↓
amazon-ecs-render-task-definition  (troca só a imagem do container, preserva todo o resto)
  ↓
[só a Api] task one-off de migração, usando a mesma imagem nova
  ↓
amazon-ecs-deploy-task-definition  (registra a nova revisão + atualiza o Service + aguarda estabilizar)
  ↓
Health check (retry com backoff)
  ↓
Smoke test
```

**Nunca reconstrói a Task Definition do zero.** `aws ecs describe-task-definition` busca a definição real já configurada no ECS — environment variables, secrets (referências ao Secrets Manager), IAM Task Role/Execution Role, portas, health check, configuração de log (`awslogs`), CPU/memória e configuração de rede continuam exatamente como estavam; `amazon-ecs-render-task-definition` só substitui o campo `image` do container indicado. Isso é o oposto de aplicar um JSON estático commitado neste repositório (que ficaria desatualizado assim que alguém ajustasse algo manualmente no Console AWS).

### Migrations (Etapa 15)

Decisão desta etapa: **nunca migrar automaticamente no startup do ECS Service** (continua assim, ver `docs/deployment.md`) — em vez disso, um **passo controlado**, uma task ECS Fargate one-off, disparada pelo próprio workflow de deploy da Api, antes de atualizar o Service:

1. A task definition recém-renderizada (já com a imagem nova) é registrada no ECS.
2. `aws ecs run-task` dispara uma única task usando essa revisão, mas com o comando do container sobrescrito para `dotnet FileSharing.Api.dll migrate` (`--overrides`) em vez do entrypoint normal.
3. Esse modo (`Program.cs`, verificado via `args.Contains("migrate")`) resolve `ApplicationDbContext` e chama `Database.MigrateAsync()`, loga o resultado, e termina o processo — nunca inicia o servidor HTTP. Fora desse argumento explícito, o comportamento de `Program.cs` é idêntico ao de antes desta etapa.
4. O workflow espera a task parar (`aws ecs wait tasks-stopped`) e falha explicitamente (`exit 1`) se o `exitCode` do container não for `0` — **o deploy do Service nunca prossegue se a migração falhar**.

**Idempotência**: `Database.MigrateAsync()` só aplica migrações ainda não registradas em `__EFMigrationsHistory` — rodar de novo (ex. um retry manual) é um no-op seguro. **Concorrência**: verificado empiricamente que o próprio migrator do Npgsql toma um `LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE` antes de aplicar qualquer migração pendente, serializando duas execuções simultâneas no nível do banco; como camada adicional, o `concurrency` do GitHub Actions (ver abaixo) já impede que dois deploys da Api rodem ao mesmo tempo. **Falha/rollback**: se a migração falhar, o job para ali — o Service ECS continua rodando a revisão/imagem anterior (nunca fica parcialmente atualizado), e a imagem nova já está no ECR disponível para diagnóstico, mas nunca chega a ser implantada.

## Health check e smoke test pós-deployment

Depois que `amazon-ecs-deploy-task-definition` já esperou o Service estabilizar (`wait-for-service-stability: true` — a action falha o job se isso não acontecer em até 10 minutos), o workflow ainda confirma diretamente, via HTTP real:

- **Api**: `GET /health/ready` (retry, até 10 tentativas com 10s de intervalo) — cobre Postgres e S3 alcançáveis a partir da task recém-implantada, não só "o ECS acha que a task está rodando". Smoke test final: `/health/live` + `/health/ready`.
- **Web**: `GET /health/live` (o Web não tem `/health/ready` — não tem dependência própria de banco/S3, ver `docs/deployment.md`). Smoke test final: `/health/live` + a landing page (`GET /`) respondendo.

Nenhum dos dois smoke tests faz upload/download real (nada destrutivo em produção) — só confirma que os processos estão respondendo.

## Rollback

**Estratégia preferencial: apontar o Service para a revisão de Task Definition anterior — nunca reconstruir uma imagem antiga.** Toda imagem já publicada permanece no ECR (sujeita à política de retenção do repositório — recomendado manter pelo menos as últimas ~20 tags de SHA, nunca `latest` sozinha, para sempre ter uma imagem anterior disponível), e toda revisão de Task Definition já registrada permanece disponível no ECS indefinidamente (revisões nunca são deletadas automaticamente).

### Como identificar o deployment anterior

```bash
# Lista as últimas revisões da família de task definitions (mais recente primeiro)
aws ecs list-task-definitions --family-prefix filesharing-api --sort DESC --max-items 5

# Confirma qual imagem (tag = commit SHA) uma revisão específica usa
aws ecs describe-task-definition --task-definition filesharing-api:<revision-anterior> \
  --query 'taskDefinition.containerDefinitions[0].image'
```

O SHA na tag da imagem já diz exatamente qual commit rodava — cruze com `git log`/o histórico de PRs merged em `main` para confirmar que é de fato a versão "boa" antes de restaurar.

### Como restaurar

```bash
# Aponta o Service diretamente para a revisão anterior (sem re-renderizar nada) — o ECS
# Service Deployment usa o mesmo mecanismo de rolling update do deploy normal.
aws ecs update-service \
  --cluster filesharing-cluster \
  --service filesharing-api \
  --task-definition filesharing-api:<revision-anterior> \
  --force-new-deployment
```

### Como validar o rollback

```bash
aws ecs wait services-stable --cluster filesharing-cluster --services filesharing-api
curl --fail https://<api-hostname>/health/ready
```

**Migração de banco durante um rollback:** se a versão sendo revertida introduziu uma migração de banco que a versão anterior não entende (ex. uma coluna `NOT NULL` nova), reverter só o código sem reverter o schema pode quebrar a versão antiga. Nenhuma migration `Down`/reversão automática é executada por este processo — reverter o schema (`dotnet ef database update <MigrationAnterior>`, rodado manualmente/via a mesma task one-off apontando para o executável antigo) é uma decisão caso a caso, não automatizada, porque a segurança de uma reversão de schema depende inteiramente do que aquela migração específica mudou.

## Hangfire e deployment

Nenhuma mudança na arquitetura do Hangfire por esta etapa (mesma decisão desde a Etapa 6/14: PostgreSQL como storage compartilhado, `[DisableConcurrentExecution]`, sem Dashboard público). O que muda com o deploy automatizado:

- **Restart do container durante o rolling update do ECS**: o `HangfireServer` da task antiga para (a task é desregistrada do Target Group, drena conexões, depois é finalizada); a task nova sobe e registra seu próprio `HangfireServer` assim que o container inicia — nenhuma configuração adicional necessária, é o comportamento padrão do Hangfire ao perder/ganhar um worker.
- **Recurring job (`expired-file-cleanup`)**: reagendado automaticamente pela nova task no startup (`UseExpiredFileCleanupSchedule`, já existente) — não depende de qual task específica está viva, só que pelo menos uma esteja.
- **Execução em andamento no momento do deploy**: se a task antiga estiver no meio de uma execução do job de limpeza quando é finalizada, `[DisableConcurrentExecution(timeoutInSeconds: 600)]` expira o lock depois de 10 minutos — a próxima execução agendada (ou uma nova task assumindo o lock liberado) simplesmente roda de novo; o job já é idempotente por design (Etapa 6 — tratar "objeto já ausente no S3" como sucesso), então uma reexecução parcial nunca corrompe estado.
- **Retries**: comportamento padrão do Hangfire, inalterado.

## SignalR e deployment

Também sem mudança de arquitetura (sem backplane, decisão já documentada em `docs/deployment.md`/`docs/security.md`). Relevante para o deploy automatizado:

- **Conexões existentes são interrompidas quando a task antiga é finalizada** — o cliente (Web: `WithAutomaticReconnect`; Mobile: reconexão do próprio `HubConnection`) já reconecta automaticamente, caindo na task nova (ou em qualquer task saudável, já que só há uma task por serviço nesta etapa — `desiredCount: 1`, ver `docs/infrastructure.md`).
- **Nenhuma mensagem duplicada**: cada notificação (`FileDownloaded`) é emitida uma única vez, no momento do download — um restart de container não reprocessa nem reemite notificações passadas (não há fila/replay de eventos).
- **Continua fora de escopo**: um backplane Redis/Azure SignalR só passaria a ser necessário se `desiredCount` da Api for aumentado para mais de 1 — não implementado nesta etapa, por pedido explícito ("não adicionar Redis/backplane só por antecipação").

## Web deployment

Deploy automatizado (mesmo fluxo do backend, sem o passo de migração). Os fluxos funcionais (Landing Page, Login, Register, Forgot/Reset Password, Dashboard, Files, Upload, File Details, History, Notifications, Profile/Settings, SignalR, navegação pública/autenticada) já são cobertos pelos testes automatizados (`FileSharing.Web.Tests`, rodados no job `test` de `web.yml`) — não são reexecutados manualmente em produção a cada deploy; o smoke test pós-deploy só confirma que o processo está de pé (`/health/live` + landing page respondendo).

## Mobile CI

Sem deployment (nenhuma loja de aplicativo, nenhum distribution build) — o objetivo desta etapa é só "o projeto continua compilável".

### Android

- Runner `ubuntu-latest` — não precisa de macOS; o workload `maui-android` já traz o Android SDK/build tools necessários via pacotes NuGet (sem precisar do Android Studio).
- `actions/setup-java` (Microsoft Build of OpenJDK 17) antes do workload, requisito conhecido do toolchain de build Android.
- Build unsigned (`-p:AndroidPackageFormat=apk`, sem `<AndroidSigningKeyStore>` configurado) — gera um APK assinado com uma chave de debug automática do próprio SDK, nunca commitada e nunca reutilizável fora do runner. **Nenhum keystore de produção existe neste repositório.** Se uma etapa futura precisar de um build assinado para distribuição, o keystore deve vir de um GitHub Secret (nunca committed), referenciado só no job de build, nunca logado.
- O APK resultante é publicado como artifact do workflow (`actions/upload-artifact`, retenção de 14 dias) — só para inspeção/QA manual, não uma distribuição real.

### iOS

- Runner `macos-15` (Xcode via `maxim-lobanov/setup-xcode@v1`, `latest-stable`) — obrigatório: `net10.0-ios` só existe em `TargetFrameworks` quando o SO do host não é Linux (`FileSharing.Mobile.csproj`), e compilar/assinar para iOS exige as ferramentas da Apple.
- Build sem `BuildIpa`, sem `CodesignKey`/`CodesignProvision` — só valida que o código compartilhado (Domain/Application/Mobile.Core/Views/ViewModels multiplataforma) compila para iOS. **Nenhum certificado de distribuição foi criado ou é necessário para esta etapa.**
- **Pendência conhecida, documentada e não resolvida por esta etapa**: a combinação exata de versão do Xcode compatível com o workload `maui-ios` do .NET 10 SDK usada aqui (`xcode-version: latest-stable`) não foi validada rodando de fato em um runner GitHub Actions (nenhum runner macOS disponível neste ambiente de desenvolvimento para testar) — só a sintaxe do workflow foi verificada (`actionlint`, ver "Validação dos workflows" abaixo). Se a primeira execução real falhar por incompatibilidade de versão, o `xcode-version` deve ser fixado a uma versão específica testada (ex. `"16.1"`) em vez de `latest-stable`.

### Código compartilhado

Nenhuma mudança de CI introduziu divergência entre Android/iOS — o mesmo `FileSharing.Mobile.csproj`/código-fonte é compilado para as duas plataformas nos dois jobs (`android-build`/`ios-build`), sem branches de código condicionais novos (`#if ANDROID`/`#if IOS`) adicionados por esta etapa.

## Cache

- **NuGet** (`~/.nuget/packages`): cache manual via `actions/cache`, chaveado por `hashFiles('Directory.Packages.props', '**/*.csproj')` — não o cache nativo do `setup-dotnet` (que exige `packages.lock.json`, inexistente neste repositório, dado o Central Package Management via `Directory.Packages.props`). Uma chave de cache corrompida/desatualizada nunca impede o workflow de rodar — `restore-keys` com prefixo permite um cache parcial, e na ausência total de cache o `dotnet restore` simplesmente busca tudo do NuGet.org normalmente (mais lento, nunca quebrado).
- **Workload MAUI** (`~/.dotnet/sdk-manifests`, `~/.dotnet/metadata/workloads`, `~/.dotnet/packs`): cache separado por plataforma (`maui-android-workload-*`/`maui-ios-workload-*`) — a instalação do workload é a etapa mais lenta do `mobile.yml`; sem cache, todo PR pagaria esse custo do zero.
- **Docker layers** (`docker/build-push-action`, `cache-from`/`cache-to: type=gha`): cache de camadas do build multi-stage entre execuções — a camada de `dotnet restore` (a mais cara) só é reconstruída quando `Directory.Packages.props`/os `.csproj` copiados mudam, exatamente como já documentado no comentário dos próprios Dockerfiles (Etapa 14).

## Concurrency

- **Nível de workflow** (`backend-${{ github.workflow }}-${{ github.ref }}`, etc.): cancela uma execução de CI supérflua quando um PR recebe um novo commit antes da execução anterior terminar (`cancel-in-progress: true` só quando `github.ref != 'refs/heads/main'`) — nunca cancela uma execução em `main` no meio de um deploy.
- **Nível do job `deploy`** (`production-deploy-api`/`production-deploy-web`, `cancel-in-progress: false`): garante que dois deploys para o mesmo serviço nunca rodam ao mesmo tempo — uma segunda execução (ex. dois merges em sequência rápida) espera a primeira terminar em vez de rodar em paralelo, o que poderia disparar duas migrações simultâneas ou dois `update-service` conflitantes.
- **Pull Requests nunca disparam deploy** (`if: github.ref == 'refs/heads/main' && github.event_name == 'push'`) — não há necessidade de concurrency específica para esse caso, porque o job de deploy simplesmente não existe na execução de um PR.

## Segurança dos workflows

- `permissions:` declarado explicitamente em cada workflow (`contents: read` no nível do workflow; `id-token: write` só no job `deploy`, nunca no workflow inteiro) — o padrão do GitHub (permissões amplas por repositório) nunca é usado implicitamente.
- Nenhum PR de fork recebe secrets/variáveis de produção — o job `deploy` só existe no trigger `push` (nunca `pull_request`), e mesmo que existisse, o Environment `production` protegido já bloquearia isso por padrão do GitHub.
- Nenhuma interpolação direta de dado não confiável em um comando shell (`${{ }}` de entrada externa como título de PR/nome de branch nunca é usado dentro de um `run:` — só valores controlados: `github.sha`, `steps.*.outputs.*`, `vars.*`/secrets configurados pelo próprio dono do repositório).
- `docker/build-push-action`/`aws-actions/*` são actions oficiais versionadas por tag major (`@v4`/`@v6`/etc.) — nunca `@main`/`@latest` de uma action de terceiros não auditada.
- Validado com `actionlint` (ver abaixo) — checa exatamente essa classe de problema (permissões excessivas, expressões malformadas, uso de secrets em contexto inseguro) além de sintaxe.

## Validação dos workflows (feita nesta etapa)

Sem um runner GitHub Actions real disponível neste ambiente de desenvolvimento, a validação foi:

1. **YAML válido** — `python3 -c "import yaml; yaml.safe_load(...)"` nos quatro arquivos.
2. **`actionlint`** (ferramenta dedicada a workflows do GitHub Actions — schema de cada action usada, expressões `${{ }}`, `permissions`, contextos disponíveis por evento) — **zero problemas encontrados** nos quatro workflows.
3. **`shellcheck`** integrado ao `actionlint` (analisa cada bloco `run:` como um script shell) — **zero problemas encontrados**.
4. **`infrastructure/docker/e2e-smoke-test.sh`** — o mesmo script usado por `infrastructure.yml` foi executado de verdade neste ambiente contra a stack Docker Compose já em execução (Etapa 14) — passou de ponta a ponta.
5. **`dotnet build`/`dotnet test`/`dotnet format --verify-no-changes`** — rodados localmente para os projetos/escopos exatos que cada workflow usa (ver "Testes executados" no relatório final desta etapa).

**O que não pôde ser validado neste ambiente** (precisa da primeira execução real no GitHub Actions): o workload MAUI Android de fato instala e compila sem erro num runner `ubuntu-latest` real; o runner `macos-15` com a versão de Xcode escolhida de fato compila `net10.0-ios`; o fluxo completo de OIDC→ECR→ECS (não há conta AWS/recursos provisionados para testar contra). Documentado explicitamente, não uma alegação de sucesso não verificada.

## Habilitando o deploy real (quando esse dia chegar)

1. Provisionar os recursos AWS descritos em `docs/infrastructure.md` (ECR, RDS, ECS Fargate + Task Definitions, ALB, ACM, Secrets Manager, Security Groups) — nenhum deles existe hoje.
2. Criar o OIDC Identity Provider + a IAM Role com a trust policy acima.
3. Configurar o GitHub Environment `production` com as variáveis listadas (`AWS_ROLE_TO_ASSUME`, `AWS_REGION`, `ECS_NETWORK_CONFIGURATION`, `API_PUBLIC_HOSTNAME`, `WEB_PUBLIC_HOSTNAME`) e, recomendado, "Required reviewers".
4. Configurar a branch protection de `main` exigindo os status checks de CI.
5. Fazer merge de um PR para `main` — o job `deploy` deixa de ser pulado automaticamente (a condição de skip só olha para `AWS_ROLE_TO_ASSUME`).

## Decisões desta etapa

- **Estrutura de workflows preservada** (por componente: backend/web/mobile/infrastructure) em vez da divisão por camada sugerida como exemplo (`ci.yml`/`docker.yml`/`deploy.yml`) — mantém o pipeline completo de cada serviço legível em um único arquivo.
- **Sem `paths:` filtrando os triggers** — simplicidade deliberada; o custo de rodar os quatro workflows sempre é aceitável no tamanho atual do projeto, e evita o risco de um path filter desatualizado silenciosamente pular uma verificação necessária.
- **`infrastructure.yml` repurposado para E2E containerizado** (em vez de ficar vazio ou duplicar o que `backend.yml`/`web.yml` já fazem) — cobre especificamente a composição dos containers (rede, env vars do Compose, host público de presigned URL), uma camada que nenhum teste unitário/de integração cobre.
- **Migração via task ECS one-off, não auto-migrate no startup** — evita o risco de múltiplas tasks tentando migrar simultaneamente no boot de um `desiredCount > 1` futuro, e mantém a migração como um passo explícito, observável e que pode falhar o deploy sem afetar o Service em produção.
- **Deploy real gated por uma variável de ambiente ausente (skip, não falha)** — porque nenhum recurso AWS existe hoje (Etapa 14 não provisionou nada); os workflows precisam continuar úteis (CI completo, imagens Docker validadas) mesmo antes de uma conta AWS real existir.
- **Um único GitHub Environment (`production`)** — sem `staging`/`development` decorativos sem infraestrutura real correspondente.

## Fora de escopo desta etapa

Provisionamento real de qualquer recurso AWS (continua só documentado, `docs/infrastructure.md`); Kubernetes/EKS/microservices; Redis/backplane SignalR; refresh tokens/JWT revocation/MFA/Google Login/App Links/Universal Links; performance/load testing (próxima etapa, conforme o enunciado desta); assinatura de produção do Mobile (keystore Android real, certificado de distribuição iOS) e publicação em loja de aplicativos.
