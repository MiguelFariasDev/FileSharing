# Development

Guia de execução local após a Etapa 14 (Docker + preparação para AWS). Cobre como subir a stack completa via Docker Compose, quais variáveis configurar, como aplicar migrações, e qual endereço usar para alcançar a Api a partir de cada tipo de cliente (navegador, emulador Android, dispositivo físico). Para a arquitetura dos containers e o porquê de cada decisão, ver `docs/architecture.md`/`docs/deployment.md`; para os recursos AWS de produção, ver `docs/infrastructure.md`.

## Pré-requisitos

- Docker + Docker Compose v2 (`docker compose`, não `docker-compose`).
- .NET SDK 10 instalado no host (para rodar migrações EF Core e os testes — não é necessário para só subir os containers).
- Um emulador Android (Android Studio) se for testar o Mobile localmente — o Mobile nunca é containerizado (ver `docs/mobile.md`).

## Subindo a stack

```bash
cd infrastructure/docker
cp .env.example .env
# edite .env: qualquer valor serve em desenvolvimento, só precisa existir.
docker compose up -d
```

Isso sobe quatro containers na rede `filesharing-net`:

| Serviço | Container | Porta no host | Papel |
|---|---|---|---|
| `postgres` | `filesharing-postgres` | `5433` → `5432` | Banco de dados (App + Hangfire) |
| `localstack` | `filesharing-localstack` | `4566` | Emulação do S3 |
| `api` | `filesharing-api` | `5105` → `8080` | ASP.NET Core Web API |
| `web` | `filesharing-web` | `5287` → `8080` | Blazor Server |

`docker compose ps` deve mostrar os quatro como `healthy` depois de alguns segundos. `api` e `web` só iniciam depois que suas dependências (`postgres`+`localstack`, e `api`, respectivamente) já estão saudáveis (`depends_on: condition: service_healthy`).

**Nunca comitar `infrastructure/docker/.env`** — já está no `.gitignore`; só `.env.example` (sem valores reais) é versionado.

## Variáveis de ambiente (`.env`)

| Variável | Uso | Observação |
|---|---|---|
| `POSTGRES_USER` | Usuário do Postgres do container | Qualquer valor em dev |
| `POSTGRES_PASSWORD` | Senha do Postgres do container | **Obrigatória** (`docker compose up` falha sem ela, propositalmente — ver `:?` em `docker-compose.yml`) |
| `POSTGRES_DB` | Nome do banco | Padrão `filesharing` se omitido |
| `JWT_SECRET_KEY` | `Jwt:SecretKey` da Api | **Obrigatória**; gere uma com `openssl rand -base64 48` |

> **A senha do Postgres só é aplicada na primeira inicialização do volume de dados.** Se você já tinha um volume `docker_filesharing-postgres-data` de uma subida anterior (por exemplo, do Postgres rodando sem Docker Compose parametrizado), mudar `POSTGRES_PASSWORD` no `.env` **não** muda a senha já gravada no banco existente — o container do Postgres só executa a inicialização (`initdb`, que é quando `POSTGRES_PASSWORD` é aplicado) quando o diretório de dados está vazio. Nesse caso, ou (a) ajuste `POSTGRES_PASSWORD` no `.env` para o valor que já está gravado no volume, ou (b) remova o volume deliberadamente (`docker volume rm docker_filesharing-postgres-data` — **destrutivo**, apaga todos os dados locais) para reinicializar do zero com a nova senha.

Nenhum desses valores deve ser reaproveitado em produção — produção usa AWS Secrets Manager (ver `docs/infrastructure.md`).

## Migrações do banco

Preservado exatamente como antes da Etapa 14 — nenhum container aplica migração automaticamente no startup (decisão deliberada, ver `docs/deployment.md`). Rode do host, contra a porta mapeada do Postgres (`5433`):

```bash
dotnet ef database update \
  -p src/FileSharing.Infrastructure \
  -s src/FileSharing.Api \
  --connection "Host=localhost;Port=5433;Database=filesharing;Username=postgres;Password=<a mesma do .env>"
```

O schema `hangfire` é criado automaticamente pelo próprio Hangfire na primeira vez que a Api sobe com `ConnectionStrings:Postgres` válido — não é parte das migrações do EF Core.

## Endereços — qual host usar em cada contexto

Esta é a fonte de confusão mais comum ao containerizar: o mesmo serviço lógico (Api, LocalStack) tem um endereço diferente dependendo de **quem** está chamando.

| De → Para | Endereço | Por quê |
|---|---|---|
| Container `api` → `postgres` | `postgres:5432` | Nome do serviço Docker, resolvido pela rede `filesharing-net` |
| Container `api` → `localstack` (chamadas reais: HeadObject/DeleteObject) | `localstack:4566` | Idem — nunca `localhost` dentro de um container |
| Container `web` → `api` | `api:8080` | Idem — o Blazor Server roda no processo do servidor, não no navegador |
| Presigned URL gerada pela Api, consumida pelo **navegador no host** | `localhost:4566` | O navegador roda fora da rede Docker; `AWS:PublicServiceURL=http://localhost:4566` no `docker-compose.yml` garante que a presigned URL seja assinada para este host, não para `localstack:4566` (ver `docs/architecture.md`, "Dois clientes `IAmazonS3`") |
| Navegador no host → Web | `http://localhost:5287` | Porta mapeada do container `web` |
| Navegador no host → Api (Swagger, chamadas diretas) | `http://localhost:5105` | Porta mapeada do container `api` |
| Emulador Android → Api | `http://10.0.2.2:5105` | `10.0.2.2` é o alias especial do emulador para o `localhost` da máquina host — **nunca** `localhost` dentro do emulador, que se referiria ao próprio emulador |
| Emulador Android → presigned URL (LocalStack) | `http://10.0.2.2:4566` | Mesma lógica — mas note que a presigned URL já vem assinada pela Api com o host configurado em `AWS:PublicServiceURL`; se esse host for `localhost`, o emulador **não conseguirá** usá-la (precisaria ser `10.0.2.2` para o cenário de emulador, o que exigiria trocar `AWS__PublicServiceURL` antes de testar contra o emulador — ver `docs/mobile.md`) |
| Dispositivo Android físico → Api/LocalStack | IP da máquina host na rede local (ex. `192.168.x.x`) | Nem `localhost` nem `10.0.2.2` funcionam a partir de um dispositivo físico — é preciso o IP real da máquina host, e `AWS__PublicServiceURL`/`Api:BaseUrl` do Mobile precisam apontar para esse IP |
| iOS (simulador ou dispositivo) → Api/LocalStack | `localhost` (simulador) ou IP da máquina host (dispositivo físico) | O simulador iOS compartilha a rede do host diretamente (diferente do emulador Android) — `localhost` funciona no simulador; um dispositivo físico segue a mesma regra do Android físico |

Trocar de cenário (emulador → dispositivo físico) é só editar `AWS__PublicServiceURL` no `docker-compose.yml` (ou `.env`) e `Resources/Raw/appsettings.json` do Mobile — nenhuma mudança de código.

## Rodando os testes

```bash
dotnet build FileSharing.slnx
dotnet test FileSharing.slnx
dotnet format FileSharing.slnx --verify-no-changes
```

`FileSharing.IntegrationTests` e `FileSharing.ApiTests` esperam Postgres/LocalStack acessíveis nas portas mapeadas do host (`5433`/`4566`) — suba a stack (`docker compose up -d`) antes de rodá-los, ou aponte para uma instância equivalente.

## Build das imagens de produção sem subir os containers

Útil para validar que o Dockerfile ainda constrói corretamente sem necessariamente rodar a stack:

```bash
cd infrastructure/docker
docker compose build api web
```

Ver `docs/infrastructure.md` para os comandos equivalentes de `tag`/`push` para o Amazon ECR (não executados automaticamente por esta etapa).

## O que o CI roda (para reproduzir localmente antes de abrir um PR)

Ver `docs/ci-cd.md` para o detalhe completo dos workflows. Localmente, antes de abrir um Pull Request, é possível reproduzir a maior parte do que `.github/workflows/` vai checar:

```bash
# Equivalente ao job "validate" de backend.yml/web.yml (escopado por projeto, nunca a .slnx
# inteira — ver docs/ci-cd.md para o porquê)
dotnet build src/FileSharing.Api/FileSharing.Api.csproj
dotnet format src/FileSharing.Api/FileSharing.Api.csproj --verify-no-changes
dotnet list src/FileSharing.Api/FileSharing.Api.csproj package --vulnerable --include-transitive

# Testes (equivalente aos jobs unit-and-api-tests/integration-tests/test)
dotnet test tests/FileSharing.UnitTests/FileSharing.UnitTests.csproj
dotnet test tests/FileSharing.ApiTests/FileSharing.ApiTests.csproj
dotnet test tests/FileSharing.IntegrationTests/FileSharing.IntegrationTests.csproj   # requer docker compose up -d + migração aplicada
dotnet test tests/FileSharing.Web.Tests/FileSharing.Web.Tests.csproj
dotnet test tests/FileSharing.Mobile.Tests/FileSharing.Mobile.Tests.csproj

# Equivalente a infrastructure.yml — build + sobe a stack + E2E real
cd infrastructure/docker
docker compose build api web
docker compose up -d
./e2e-smoke-test.sh
docker compose down -v
```

## Encerrando

```bash
docker compose down          # para os containers, preserva os volumes (dados persistem)
docker compose down -v       # também remove os volumes (Postgres e LocalStack voltam vazios)
```
