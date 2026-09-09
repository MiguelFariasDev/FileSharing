# Arquitetura

Documenta as decisões arquiteturais até a Etapa 3 (Domain/Database, Autenticação/JWT, Upload de arquivos).
Rate limiting de link público, dashboard, SignalR, Hangfire e expiração automática pertencem a etapas futuras.

## Camadas (Clean Architecture)

```
Domain
   ↑
Application
   ↑
Infrastructure
   ↑
Api
```

- **Domain** (`FileSharing.Domain`): entidades (`User`, `File`, `Download`) e regras de negócio puras. Zero dependência de EF Core, ASP.NET Core ou AWS SDK.
- **Application** (`FileSharing.Application`): DTOs, validators (FluentValidation), serviços de caso de uso (`AuthService`, `FileUploadService`) e as abstrações que Infrastructure implementa (`IApplicationDbContext`, `IPasswordHasher`, `IJwtTokenGenerator`, `IFileStorageService`). Não conhece `AmazonS3Client`, `Npgsql` ou qualquer tipo do `Microsoft.AspNetCore.*`.
- **Infrastructure** (`FileSharing.Infrastructure`): implementações concretas — `ApplicationDbContext` (EF Core/PostgreSQL), `PasswordHasher`/`JwtTokenGenerator` (Identity/JWT), `S3FileStorageService` (AWSSDK.S3).
- **Api** (`FileSharing.Api`): controllers finos, configuração de DI (pasta `Extensions/`), pipeline HTTP, Swagger.
- **Mobile** (`FileSharing.Mobile`): cliente .NET MAUI (Android). Conversa com a Api via HTTP/JSON; nunca referencia o AWS SDK nem possui credenciais AWS.

Sem MediatR: cada caso de uso é um serviço de Application chamado diretamente pelo controller (`IAuthService`, `IFileUploadService`), decisão tomada na Etapa 2 e mantida na Etapa 3 por consistência.

## Fluxo de upload (Etapa 3)

O arquivo **nunca** passa pela API — apenas metadata. O upload em si é sempre direto do cliente para o S3.

```
MAUI                          Api                              S3 (LocalStack/AWS)
 |                             |                                   |
 |--- POST /api/files/upload ->|                                   |
 |    (metadata apenas)        |--- cria File (PendingUpload) ---> |
 |                             |--- gera presigned PUT URL -------> |
 |<---- fileId + uploadUrl ----|                                   |
 |                                                                  |
 |------------------------- PUT uploadUrl (bytes do arquivo) ----->|
 |<------------------------------------------------------- 200 ----|
 |                             |                                   |
 |--- POST /api/files/{id}/complete ->                             |
 |                             |--- HEAD/GetObjectMetadata ------->|
 |                             |<---- tamanho + content-type ------|
 |                             |--- valida e ativa File ---------->|
 |<---- File Active, CreatedAt, ExpiresAt = CreatedAt+24h ---------|
```

Pontos importantes:

- **`PendingUpload` não é um arquivo disponível.** Se o PUT ao S3 nunca acontecer, ou o cliente cancelar, o `File` permanece `PendingUpload` para sempre (a limpeza de uploads abandonados é uma etapa futura) — nunca vira `Active` por acidente.
- **`CreatedAt`/`ExpiresAt` só existem depois do `complete`.** A janela de 24 horas começa na confirmação do upload, nunca na criação da presigned URL (`FileSharing.Domain.Entities.File.CompleteUpload`).
- **A API nunca confia no cliente.** `POST /api/files/{id}/complete` consulta o S3 (tamanho e Content-Type reais do objeto) antes de ativar o arquivo; se o objeto não existir ou os metadados não baterem, o arquivo continua `PendingUpload`.
- **`StorageKey` é opaco e aleatório** (`RandomTokenGenerator`, `RandomNumberGenerator` de 256 bits, base64url) — nunca o nome original do arquivo, nunca previsível.

## Pastas → ZIP

Uma pasta é sempre representada por **um único** `File` (`IsFolder = true`, `CompressionType = Zip`), nunca por múltiplos registros — um `File` por arquivo dentro da pasta destruiria a noção de "uma pasta compartilhada".

A compactação acontece **no cliente MAUI**, nunca na API:

```
MAUI: pasta selecionada (Storage Access Framework)
  → enumera recursivamente (preserva estrutura relativa e nome raiz)
  → cria .zip local (System.IO.Compression, CompressionLevel.Optimal — lossless)
  → upload do .zip via o mesmo fluxo de presigned URL de um arquivo comum
  → remove o .zip temporário do dispositivo ao final (sucesso, erro ou cancelamento)
```

Se a compactação acontecesse na API, cada byte da pasta passaria pelo backend antes de ir para o S3 — exatamente o gargalo que o upload direto via presigned URL existe para evitar.

Arquivos individuais (PDF, imagem, vídeo, áudio, EPUB) **nunca são recomprimidos** — o Content-Type declarado já corresponde a formatos tipicamente comprimidos (JPEG, MP4, MP3 etc.); o byte a byte do arquivo original é preservado integralmente até o S3. Nenhuma etapa do fluxo usa Base64 — o conteúdo é sempre enviado como bytes crus no corpo do `PUT`.

## Storage abstraction

```
Application.Abstractions.Storage.IFileStorageService
  - CreatePresignedUploadUrlAsync
  - ObjectExistsAsync
  - GetObjectMetadataAsync
  - DeleteObjectAsync

Infrastructure.Storage.S3FileStorageService : IFileStorageService   (AWSSDK.S3)
```

Trocar LocalStack ↔ AWS real é só configuração (`AWS:ServiceURL` presente vs. ausente em `appsettings.*.json`/variáveis de ambiente) — nenhum código de Application ou Domain sabe que LocalStack existe. Ver `docs/database.md`/seção "LocalStack" abaixo e `infrastructure/docker/docker-compose.yml`.

> **Nota de compatibilidade (AWSSDK.S3 v4):** a partir da v4 do SDK, `AmazonS3Config.ServiceURL` sozinho não é mais suficiente para redirecionar as chamadas para um endpoint customizado — o SDK passou a exigir a variável de ambiente `AWS_ENDPOINT_URL_S3` (ou `AWS_ENDPOINT_URL`) para isso. `FileSharing.Api.Extensions.StorageExtensions` já define essa variável automaticamente quando `AWS:ServiceURL` está configurado; isso foi validado manualmente contra o LocalStack real durante o desenvolvimento desta etapa.

Multipart upload não foi implementado nesta etapa (fora de escopo), mas a abstração (`IFileStorageService`) não impede adicioná-lo depois — seria um novo método (`CreateMultipartUploadAsync`/`CompletePartAsync`) na mesma interface, sem alterar Application ou Api.

## Diagrama de pastas do backend

```
src/
  FileSharing.Domain/
    Entities/{User,File,Download}.cs
    Enums/{FileStatus,CompressionType}.cs
  FileSharing.Application/
    Abstractions/{Persistence,Security,Storage}/
    Common/{Result,FileTypePolicy,RandomTokenGenerator}.cs
    DTOs/{Auth,Files}/
    Validators/{Auth,Files}/
    Services/{Auth,Files}/
  FileSharing.Infrastructure/
    Persistence/{ApplicationDbContext,Configurations,Migrations}/
    Identity/{PasswordHasher,JwtTokenGenerator}.cs
    Storage/S3FileStorageService.cs
  FileSharing.Api/
    Controllers/{AuthController,FilesController}.cs
    Extensions/{Persistence,Auth,Storage,Swagger,ValidationResult,ClaimsPrincipal}Extensions.cs
  FileSharing.Mobile/
    Models/UploadableItem.cs
    Services/Api/FileSharingApiClient.cs
    Services/Upload/{IFilePickerService,IFolderPickerService,IFileUploadService,ProgressReportingStream}.cs
    Platforms/Android/{FolderPickerService,ActivityResultBridge}.cs
    ViewModels/UploadViewModel.cs
```
