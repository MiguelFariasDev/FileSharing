# Deployment

Este documento cobre o que as Etapas 6 (Hangfire), 7 (SignalR), 14 (Docker + preparação para AWS) e 15 (CI/CD) introduzem com relevância para execução/deploy. Para o passo a passo de execução local (Docker Compose) ver `docs/development.md`; para os recursos AWS preparados (ECR, S3, RDS, ECS Fargate, ALB, ACM, Secrets Manager, CloudWatch, IAM, Security Groups, custo) ver `docs/infrastructure.md`; para os workflows do GitHub Actions (triggers, OIDC, rollback, migrations no deploy) ver `docs/ci-cd.md`. A execução real de qualquer deploy em AWS continua fora de escopo — os workflows de CD existem e foram validados sintaticamente, mas se auto-desabilitam (skip) até que os recursos AWS reais e a IAM Role OIDC sejam provisionados (nenhum foi, ver `docs/infrastructure.md`).

## Hangfire em produção

- **Storage:** PostgreSQL — a mesma instância/base já usada por `ApplicationDbContext` (`ConnectionStrings:Postgres`), via `Hangfire.PostgreSql`. Nenhuma infraestrutura adicional (Redis, segunda base) é necessária. Ver `docs/database.md`.
- **Server:** `services.AddHangfireServer()` registra o processamento de jobs **no mesmo processo** da API (não um worker separado) — cada instância da API que subir também roda um `HangfireServer`. Múltiplas instâncias rodando simultaneamente é seguro: `[DisableConcurrentExecution]` (no método da interface `IExpiredFileCleanupJob`) usa o lock distribuído do próprio Hangfire, apoiado no PostgreSQL compartilhado, para garantir que só uma execução do job `expired-file-cleanup` rode por vez em todo o cluster.
- **Dashboard:** não exposto nesta etapa. `app.UseHangfireDashboard()` nunca é chamado — `/hangfire` não é uma rota válida. Se uma etapa futura precisar do dashboard, ele deve ser adicionado atrás de autenticação/autorização (nunca anônimo), nunca simplesmente reativado sem esse filtro.
- **Habilitar/desabilitar:** `ExpirationCleanup:Enabled` (padrão `true`). Quando `false`, ou quando `ConnectionStrings:Postgres` não está configurado, a aplicação sobe normalmente sem registrar storage/server/job do Hangfire — `IExpiredFileCleanupJob` continua resolvível via DI (para uso direto/testes), apenas nunca agendado.

## Configuração obrigatória

Igual ao padrão já estabelecido para `Jwt:SecretKey` e as credenciais AWS (ver `docs/security.md`): nenhum segredo novo foi introduzido por esta etapa — o Hangfire reaproveita a connection string `Postgres` já exigida desde etapas anteriores. Variáveis relevantes (nenhuma é um segredo por si só):

```json
"ExpirationCleanup": {
  "Enabled": true,
  "IntervalMinutes": 15,
  "BatchSize": 100
}
```

- `IntervalMinutes`: usado para montar a expressão cron (`*/{IntervalMinutes} * * * *`) do job recorrente `expired-file-cleanup`. Aproximado, não depende de precisão de segundo.
- `BatchSize`: máximo de arquivos expirados processados por execução — limita memória e duração de cada execução; um backlog maior drena ao longo de execuções subsequentes.

## O que verificar antes de subir em um ambiente novo

1. `ConnectionStrings:Postgres` aponta para um PostgreSQL alcançável a partir do processo da API (o mesmo já exigido para `ApplicationDbContext`).
2. O usuário do banco tem permissão para criar o schema `hangfire` na primeira subida (o mesmo usuário já usado pelo EF Core é suficiente em desenvolvimento; em produção, confirmar que a role tem `CREATE SCHEMA`).
3. `ExpirationCleanup:Enabled` está `true` (ou ausente — o padrão já é `true`) nos ambientes onde a limpeza automática deve rodar.
4. Nenhuma rota `/hangfire` foi adicionada manualmente sem um filtro de autorização.
5. Nenhuma rota `/hubs/notifications` foi exposta sem `[Authorize]` no Hub.

## Containerização (Etapa 14)

- **Imagens multi-stage** (`src/FileSharing.Api/Dockerfile`, `src/FileSharing.Web/Dockerfile`): estágio de build usa o SDK (`mcr.microsoft.com/dotnet/sdk:10.0-noble`), nunca publicado; estágio final usa só o runtime ASP.NET (`mcr.microsoft.com/dotnet/aspnet:10.0-noble`), sem SDK, sem código-fonte. Cada Dockerfile copia e restaura apenas os projetos que aquele executável realmente referencia (Api: Domain+Application+Infrastructure+Api; Web: Domain+Application+Web) — Mobile/tests nunca entram no contexto de build (reforçado por `.dockerignore` na raiz do repositório).
- **Usuário não-root.** A imagem oficial `aspnet:10.0` já roda como o usuário `app` (não-root) desde o .NET 8 — declarado explicitamente via `USER app` em vez de depender implicitamente do padrão da imagem base.
- **Sem segredo nenhum na imagem.** Nenhum Dockerfile define `ENV` com senha, connection string ou chave — toda configuração chega em tempo de execução via variável de ambiente (Docker Compose localmente, Secrets Manager/Task Definition em produção — ver `docs/infrastructure.md`).
- **Porta configurável.** `ASPNETCORE_HTTP_PORTS=8080` (convenção moderna do .NET, substitui `ASPNETCORE_URLS`); `ASPNETCORE_URLS` nunca é hardcoded, então a mesma imagem funciona tanto sob a porta 8080 (convenção usada em Docker Compose/ECS neste projeto) quanto sob outra porta que um orquestrador queira atribuir.
- **`HEALTHCHECK` via `curl` contra `/health/live`** (liveness, nunca `/health/ready` — ver seção de Health Checks abaixo) — `curl` é instalado explicitamente no estágio final porque a imagem runtime mínima não traz nenhum cliente HTTP por padrão.
- **Migração de banco nunca acontece automaticamente no startup do container/ECS Service.** Localmente, continua manual (`dotnet ef database update` a partir do host, ver `docs/development.md`). Em produção (Etapa 15, ver `docs/ci-cd.md`), o workflow de deploy da Api dispara uma **task ECS Fargate one-off** que roda a mesma imagem recém-publicada com o comando sobrescrito para `dotnet FileSharing.Api.dll migrate` — um modo dedicado em `Program.cs` (verificado via `args.Contains("migrate")`) que só chama `Database.MigrateAsync()` e termina, nunca inicia o servidor HTTP. Continua sendo um passo controlado e explícito (nunca "a cada container startup"), só que agora automatizado como parte do deploy em vez de manual — decisão tomada porque uma task ECS one-off consegue alcançar o RDS (dentro da mesma VPC/Security Group) enquanto o runner do GitHub Actions não conseguiria (RDS nunca é publicamente acessível).

### Docker Networking

- **Nunca `localhost` entre containers** — cada serviço se conhece pelo nome definido em `docker-compose.yml` (`postgres`, `localstack`, `api`) via a rede nomeada `filesharing-net`; `localhost` dentro de um container sempre se refere ao próprio container, nunca a outro serviço.
- `depends_on: condition: service_healthy` garante que a Api só inicia depois que Postgres e LocalStack já responderam saudável ao respectivo healthcheck — evita a corrida de conectar antes do Postgres aceitar conexões.
- Ver `docs/development.md` para a tabela completa de qual endereço usar em cada contexto (container-para-container, navegador no host, emulador Android, dispositivo físico).

### Upload direto para S3 preservado

Containerizar a Api não alterou o modelo de upload direto ao S3 (Etapas 3/5): o conteúdo do arquivo nunca passa pelo container da Api, nem para arquivos individuais nem para pastas comprimidas em ZIP pelo cliente. O único ajuste necessário foi o cliente de presign dedicado (`AWS:PublicServiceURL`) descrito em `docs/architecture.md`, porque agora o LocalStack também é um container — sem esse ajuste, uma presigned URL assinada com o endpoint interno (`localstack:4566`) seria inútil para um cliente fora da rede Docker.

## SignalR em produção (Etapa 7)

- **Sem backplane nesta etapa.** O SignalR usa seu armazenamento de conexões em memória padrão — sem Redis, sem Azure SignalR Service, conforme pedido explicitamente para esta etapa.
- **Funciona corretamente com uma única instância da Api.** Toda a garantia de isolamento por usuário (`Clients.User`) e suporte a múltiplas conexões do mesmo usuário (várias abas/dispositivos) funciona sem nenhuma configuração adicional enquanto só uma instância do processo estiver rodando.
- **Limitação conhecida para múltiplas instâncias.** Se uma implantação futura rodar mais de uma instância da Api simultaneamente atrás de um load balancer (por exemplo, várias tasks no AWS ECS Fargate mencionado no `CLAUDE.md`), uma conexão SignalR estabelecida com a instância A não é visível pela instância B — um download processado pela instância B não conseguiria notificar em tempo real um dono cuja conexão de navegador está aberta com a instância A (a notificação simplesmente não chegaria; nada quebra, nada vaza para o usuário errado). Isso é puramente uma limitação de alcance/entrega, não de segurança: o isolamento por usuário continua correto independentemente da topologia.
- **Solução para múltiplas instâncias: um backplane.** Redis (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) ou o Azure SignalR Service gerenciado são as opções padrão do ecossistema ASP.NET Core para sincronizar mensagens entre instâncias. **Não implementado nesta etapa** — é uma decisão de infraestrutura de deployment/escala a ser tomada quando (e se) a Api passar a rodar com mais de uma instância simultânea, não algo que precisa existir para o funcionamento correto hoje.
- **Sticky sessions não são necessárias com um backplane**, mas **são necessárias sem um** se o load balancer não garantir afinidade de conexão — sem backplane e sem sticky sessions, uma reconexão do cliente poderia cair em uma instância diferente da que originalmente aceitou a conexão, o que já é tratado pelo protocolo de negociação do SignalR (uma nova conexão é sempre válida), mas o cliente perderia qualquer estado em memória associado à conexão anterior. Sem estado em memória por conexão nesta implementação (o Hub não guarda nada), o impacto prático disso hoje é nulo.
- **Estratégia para o primeiro deploy em ECS Fargate (Etapa 14): uma única task por serviço.** Enquanto a Api roda com `desiredCount: 1` (ver `docs/infrastructure.md`), a limitação de múltiplas instâncias acima simplesmente não se manifesta — todas as conexões SignalR e todo processamento de download acontecem na mesma task. Escalar a Api horizontalmente (`desiredCount > 1`) **exige** resolver a limitação de backplane primeiro (Redis ou Azure SignalR Service) — isso não foi implementado nesta etapa porque o pedido explícito da Etapa 14 é preparar o deploy, não adicionar Redis "por antecipação". Documentado aqui como um pré-requisito real para quando esse escalonamento for necessário, não uma omissão silenciosa.
- **Validado dentro do Docker Compose local desta etapa:** negociação (`POST /hubs/notifications/negotiate`), conexão autenticada via JWT (`?access_token=`), e entrega de `FileDownloaded` funcionam sem alteração dentro da rede `filesharing-net` — o Hub não tem nenhuma dependência de rede além do que já existia antes da containerização.
- **Compatibilidade com ALB:** o Target Group da Api precisa ter os *stickiness* do ALB (se usado) e o timeout ocioso configurados de forma compatível com conexões de longa duração (WebSocket); o ALB por padrão já suporta upgrade de WebSocket transparentemente para o target, desde que o Security Group/health check não derrube a conexão. Ver `docs/infrastructure.md` (seção ALB) para a configuração concreta recomendada.

## Health Checks e Observabilidade (Etapa 12)

- **`GET /health/live`** — liveness. Sem dependência de PostgreSQL/S3; um orquestrador (ECS, Kubernetes) deve usar isso para decidir se **reinicia** o processo, nunca a readiness abaixo.
- **`GET /health/ready`** — readiness (PostgreSQL + S3/LocalStack). Um orquestrador deve usar isso para decidir se **envia tráfego** para esta instância — nunca para decidir reiniciá-la.
- Nenhum dos dois exige autenticação (`AllowAnonymous()`), nenhum expõe connection string/exceção no corpo (resposta padrão do framework: só `"Healthy"`/`"Unhealthy"`). Ver `docs/architecture.md` para a implementação de cada check.
- **Configuração equivalente em produção:** o mesmo `IAmazonS3`/`ConnectionStrings:Postgres` já usados pelo restante da aplicação — nenhuma credencial nova, nenhuma configuração própria dos health checks.
- **Web também ganhou `/health/live` nesta etapa** (Etapa 14) — não existia nenhum endpoint de health check no `FileSharing.Web` até então. Só liveness (o Web não tem dependência de banco/S3 própria a verificar); usado pelo `HEALTHCHECK` do Dockerfile e, em produção, pelo health check do Target Group do ALB.
- **Adequado para ECS/ALB:** um Target Group de ALB (Etapa 14, ver `docs/infrastructure.md`) deve apontar para `/health/live` de cada serviço (Api e Web) — nunca `/health/ready` para o Target Group, pelo mesmo motivo que o Docker `HEALTHCHECK` também usa só liveness: um ALB que decide **remover uma task saudável do tráfego** porque o Postgres está temporariamente lento é aceitável (readiness), mas um ECS que decide **matar e recriar a task** pelo mesmo motivo (o que aconteceria se o healthcheck do container usasse `/health/ready`) só pioraria uma instabilidade temporária de uma dependência externa.

### Forwarded Headers — pré-requisito para operar atrás do ALB (Etapa 14)

Auditoria desta etapa encontrou uma lacuna real: nem a Api nem o Web tinham `UseForwardedHeaders` configurado. Sem isso, atrás de um Application Load Balancer (que sempre fala HTTP simples com o ECS — a terminação TLS acontece no ALB, ver `docs/infrastructure.md`), `HttpContext.Connection.RemoteIpAddress` passaria a refletir sempre o IP interno do ALB, nunca o IP real do cliente — quebrando silenciosamente:

- O particionamento por IP do rate limiting (`Auth`, `PasswordReset` — todo tráfego pareceria vir de um único "cliente", o próprio ALB).
- O IP registrado em `downloads.IpAddress` (Etapa 5) — auditoria de quem baixou um arquivo ficaria inútil (sempre o IP do ALB).
- A detecção de HTTPS original para `UseHttpsRedirection`/HSTS (o ASP.NET Core veria toda requisição como HTTP simples, mesmo quando o cliente usou HTTPS até o ALB).

**Corrigido** em ambos `Program.cs` (Api e Web): `UseForwardedHeaders` configurado logo após `app.Build()`, antes de qualquer outro middleware. `KnownIPNetworks`/`KnownProxies` são explicitamente limpos (`.Clear()`, não o inicializador de coleção `= { }`, que é um no-op sobre uma lista já populada) — a aplicação passa a confiar nos headers `X-Forwarded-For`/`X-Forwarded-Proto` de qualquer origem. Isso é seguro **não** porque a aplicação valida a origem do header, mas porque o Security Group do ECS (ver `docs/infrastructure.md`) só aceita tráfego de entrada vindo do próprio ALB — a fronteira de confiança é de rede, não de aplicação, já que o ALB não tem um IP fixo conhecido antecipadamente para colocar numa allowlist. Localmente (sem proxy na frente), esses headers simplesmente não chegam nunca — o comportamento observado não muda.

Nenhum backend externo (CloudWatch, X-Ray, Grafana) foi configurado nesta etapa — deliberado, ver `docs/security.md`. O que já está pronto para quando essa etapa futura chegar:

- **Logs → CloudWatch Logs**: a aplicação já escreve para `stdout`/`stderr` (console) de forma estruturada; num ambiente ECS Fargate, o driver de log `awslogs` do próprio ECS já encaminha isso para CloudWatch Logs sem nenhuma mudança de código — só configuração da Task Definition.
- **Métricas → CloudWatch Metrics/EMF**: `AppMetrics` (`System.Diagnostics.Metrics`) já expõe tudo sob o Meter `"FileSharing.Application"`; um exportador (`OpenTelemetry.Exporter.*` ou o formato EMF do CloudWatch) poderia ser ligado a esse Meter existente sem alterar `AppMetrics` nem os pontos de chamada.
- **Traces → AWS X-Ray**: o ASP.NET Core já emite `Activity`/`DiagnosticSource` para cada requisição automaticamente (nenhum código deste projeto) — um `ActivityListener`/instrumentação X-Ray se conectaria a isso do mesmo jeito que se conectaria a qualquer app ASP.NET Core.
- **Health checks → ALB/ECS**: `/health/live` e `/health/ready` já existem no formato que um Target Group de Application Load Balancer ou um `healthCheck` de Task Definition do ECS espera (200 = saudável, texto simples).

## Mobile (Etapa 13)

Documentação completa em `docs/mobile.md`. Não há infraestrutura própria a implantar para o Mobile — é um APK Android que só precisa saber a URL da Api/Hub (`src/FileSharing.Mobile/Resources/Raw/appsettings.json`, um arquivo, dois valores, nenhum segredo). Trocar de ambiente (emulador → dispositivo físico → produção) é editar esse arquivo e recompilar; nenhuma outra configuração de infraestrutura é necessária no lado do Mobile.

## Fora de escopo desta e de etapas anteriores

- **Implementado/preparado na Etapa 14** (não mais fora de escopo — ver `docs/infrastructure.md` para o detalhe de cada item): Docker Compose local (Api+Web+Postgres+LocalStack), Dockerfiles de produção para Api e Web, Forwarded Headers, cliente de presign dedicado para host público, e a documentação/preparação (nunca a execução) de AWS ECR, S3 de produção, RDS PostgreSQL, ECS Fargate, Application Load Balancer, ACM, Secrets Manager, CloudWatch, IAM e Security Groups.
- **Continua fora de escopo:** execução real de qualquer deploy em AWS (nenhum recurso foi de fato criado/provisionado — só documentado com os comandos/definições necessários), CI/CD (`.github/workflows/`), backplane Redis/Azure SignalR para SignalR multi-instância, backend externo de observabilidade (CloudWatch Metrics/X-Ray/Grafana/Datadog além do já preparado para logs), Infrastructure as Code via ferramenta dedicada (Terraform/CDK/CloudFormation — decisão desta etapa foi documentação-first, ver `docs/infrastructure.md`), performance/load testing.
