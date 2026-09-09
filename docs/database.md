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
| AccessTokenHash | varchar(64), nullable | **nulo até a Etapa 4** (link público) — não existe token de acesso enquanto o arquivo é só `PendingUpload`/`Active` sem link gerado; índice único (permite múltiplos `NULL` no Postgres) |
| Status | integer (enum) | `PendingUpload = 0`, `Active = 1`, `Expired = 2` |
| CreatedAt | timestamptz, nullable | **nulo enquanto `PendingUpload`**; definido no momento exato da confirmação do upload (`File.CompleteUpload`) |
| ExpiresAt | timestamptz, nullable | **nulo enquanto `PendingUpload`**; sempre `CreatedAt + 24h` quando definido; índice (para a futura rotina de expiração) |

### `downloads`

| Coluna | Tipo | Observações |
|---|---|---|
| Id | uuid | PK |
| FileId | uuid | FK → `files.Id`, cascade delete |
| DownloadedAt | timestamptz | obrigatório |
| IpAddress | varchar(45) | obrigatório |
| UserAgent | varchar(1000) | obrigatório |

(`downloads` ainda não é escrita por nenhum fluxo até a Etapa 3 — pertence à etapa de download público.)

## Por que `CreatedAt`/`ExpiresAt`/`AccessTokenHash` são nuláveis

Na Etapa 1, `File` era criado já `Active`. A partir da Etapa 3, `File` nasce `PendingUpload` (registro criado + presigned URL emitida, upload ainda não confirmado) e só vira `Active` quando `POST /api/files/{id}/complete` confirma o objeto no S3. Um arquivo `PendingUpload` genuinamente não tem "quando foi criado (efetivamente)" nem "quando expira" — por isso essas colunas passaram a aceitar `NULL`, em vez de receber um valor provisório que seria enganoso.

## Migrations

```bash
# Criar uma nova migration
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add <Nome> -p src/FileSharing.Infrastructure -s src/FileSharing.Api -o Persistence/Migrations

# Aplicar no banco local
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update -p src/FileSharing.Infrastructure -s src/FileSharing.Api
```

`ASPNETCORE_ENVIRONMENT=Development` é necessário para que a connection string de `appsettings.Development.json` seja carregada — sem ela, `AddPersistence` não configura nenhum provider (ver `docs/security.md`, "por que a API falha sem configuração").

Migration existente: `InitialCreate` (Etapa 3) — cria `users`, `files` (já com todos os campos da Etapa 3) e `downloads` em um único migration inicial, já que nenhuma migration havia sido criada nas Etapas 1/2.

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
