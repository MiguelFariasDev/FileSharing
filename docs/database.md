# Banco de dados

PostgreSQL via Entity Framework Core (`Npgsql.EntityFrameworkCore.PostgreSQL`). O PostgreSQL armazena **somente metadata** — o conteúdo dos arquivos fica no S3 (ver `docs/architecture.md`).

## Tabelas

### `users`

| Coluna | Tipo | Observações |
|---|---|---|
| Id | uuid | PK |
| Email | varchar(320) | obrigatório, índice único |
| PasswordHash | text | obrigatório — nunca a senha em texto puro |
| CreatedAt | timestamptz | obrigatório |

### `files`

| Coluna | Tipo | Observações |
|---|---|---|
| Id | uuid | PK |
| UserId | uuid | FK → `users.Id`, cascade delete |
| OriginalFileName | varchar(255) | nome fornecido pelo usuário — **nunca** usado como `StorageKey` |
| StorageKey | varchar(500) | chave opaca e aleatória no S3 (`RandomTokenGenerator`, 256 bits) |
| ContentType | varchar(255) | validado contra `FileTypePolicy` no initiate |
| SizeBytes | bigint | declarado no initiate, **reconfirmado** contra o S3 no complete |
| IsFolder | boolean | `true` quando o `File` representa uma pasta compactada |
| CompressionType | integer (enum) | `None = 0`, `Zip = 1` — sempre `Zip` quando `IsFolder = true`, sempre `None` quando `false` |
| AccessTokenHash | varchar(64), nullable | hash SHA-256 (hex) do token público de acesso, `NULL` até que `POST /api/files/{id}/link` gere um link (Etapa 4) — nunca o token em texto puro; índice **único** (permite múltiplos `NULL` no Postgres, já que a maioria dos arquivos nunca terá um link gerado) |
| Status | integer (enum) | `PendingUpload = 0`, `Active = 1`, `Expired = 2` |
| CreatedAt | timestamptz, nullable | **nulo enquanto `PendingUpload`**; definido no momento exato da confirmação do upload (`File.CompleteUpload`) |
| ExpiresAt | timestamptz, nullable | **nulo enquanto `PendingUpload`**; sempre `CreatedAt + 24h` quando definido; índice (para a futura rotina de expiração) |

### `downloads`

| Coluna | Tipo | Observações |
|---|---|---|
| Id | uuid | PK |
| FileId | uuid | FK → `files.Id`, cascade delete |
| DownloadedAt | timestamptz | obrigatório, UTC — capturado no momento em que o download é autorizado (ver `docs/architecture.md`) |
| IpAddress | varchar(45) | obrigatório — `HttpContext.Connection.RemoteIpAddress` (IP real do peer TCP, não um header); 45 já é o tamanho máximo de uma representação IPv6 canônica |
| UserAgent | varchar(1000) | obrigatório — `Request.Headers.UserAgent`; `"unknown"` quando ausente, truncado em `Download.MaxUserAgentLength` (1000) quando maior que o limite da coluna |

Escrita, desde a Etapa 5, por `GET /api/public/files/{token}/download` (`FileSharing.Application.Services.Files.FileDownloadService`) — um registro por chamada bem-sucedida, nunca por uma chamada rejeitada (token inválido/expirado, arquivo não `Active`, ou objeto ausente no S3).

## Por que `CreatedAt`/`ExpiresAt`/`AccessTokenHash` são nuláveis

Na Etapa 1, `File` era criado já `Active`. A partir da Etapa 3, `File` nasce `PendingUpload` (registro criado + presigned URL emitida, upload ainda não confirmado) e só vira `Active` quando `POST /api/files/{id}/complete` confirma o objeto no S3. Um arquivo `PendingUpload` genuinamente não tem "quando foi criado (efetivamente)" nem "quando expira" — por isso essas colunas passaram a aceitar `NULL`, em vez de receber um valor provisório que seria enganoso. `AccessTokenHash` segue o mesmo raciocínio: a maioria dos arquivos `Active` nunca chega a ter um link gerado, então "nenhum token ainda" é `NULL`, não uma string vazia/sentinela.

## Migrations

```bash
# Criar uma nova migration
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add <Nome> -p src/FileSharing.Infrastructure -s src/FileSharing.Api -o Persistence/Migrations

# Aplicar no banco local
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update -p src/FileSharing.Infrastructure -s src/FileSharing.Api
```

`ASPNETCORE_ENVIRONMENT=Development` é necessário para que a connection string de `appsettings.Development.json` seja carregada — sem ela, `AddPersistence` não configura nenhum provider (ver `docs/security.md`, "por que a API falha sem configuração").

Migration existente: `InitialCreate` (Etapa 3) — cria `users`, `files` (já com todos os campos, incluindo `AccessTokenHash` e seu índice único, antecipando a Etapa 4, e um índice em `ExpiresAt` antecipando a rotina de expiração da Etapa 6) e `downloads` (já com todos os campos usados pela Etapa 5: `FileId`, `DownloadedAt`, `IpAddress`, `UserAgent`) em um único migration inicial, já que nenhuma migration havia sido criada nas Etapas 1/2. Nem a Etapa 4 (link público), nem a Etapa 5 (download), nem a Etapa 6 (Hangfire/expiração) **precisaram de uma nova migration do EF Core** — o modelo de dados da aplicação já estava completo desde a `InitialCreate`; cada uma adicionou só código de aplicação/infraestrutura (`FilePublicLinkService`, `FileDownloadService`, `ExpiredFileCleanupJob`, endpoints).

## Hangfire (Etapa 6)

O Hangfire (`Hangfire.PostgreSql`) usa a **mesma** instância/base `filesharing` como seu job storage — não um segundo banco, não Redis. Ao iniciar, ele cria e mantém automaticamente seu próprio schema `hangfire` (tabelas como `hangfire.job`, `hangfire.set`, `hangfire.hash`, `hangfire.server` etc.) na mesma base física, completamente separado do schema `public` usado pelo EF Core/`ApplicationDbContext`:

```sql
-- schemas na base "filesharing" depois que a Api sobe pela primeira vez com Hangfire:
-- public    -> users, files, downloads (EF Core / ApplicationDbContext)
-- hangfire  -> job, set, hash, server, ... (gerenciado pela própria Hangfire.PostgreSql)
```

Esse schema **não** é gerenciado pelas migrations do EF Core (`dotnet ef migrations add ...`) — a própria biblioteca Hangfire.PostgreSql instala/atualiza seu schema automaticamente na primeira vez que a aplicação sobe apontando para aquele banco. Nenhuma migration do EF Core foi criada para esta etapa: o modelo de dados da aplicação (`users`/`files`/`downloads`) não mudou.

## LocalStack (S3) e PostgreSQL locais

```bash
cp src/FileSharing.Api/appsettings.Development.json.example src/FileSharing.Api/appsettings.Development.json

cd infrastructure/docker
docker compose up -d
```

Isso sobe:
- **PostgreSQL** na porta `5433` do host (não `5432`, para não colidir com um PostgreSQL local já instalado) — usuário/senha `postgres`/`postgres`, banco `filesharing`.
- **LocalStack** (S3) na porta `4566`, com o bucket de desenvolvimento `filesharing-dev` criado automaticamente (privado, com Public Access Block habilitado) pelo script `infrastructure/docker/localstack-init/01-create-bucket.sh`.

Essas portas/credenciais já estão em `src/FileSharing.Api/appsettings.Development.json` (não são segredos reais — são valores de desenvolvimento local válidos apenas para o LocalStack).
