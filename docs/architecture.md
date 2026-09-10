# Arquitetura

Documenta as decisões arquiteturais até a Etapa 5 (Domain/Database, Autenticação/JWT, Upload de arquivos, Link público de acesso, Download + histórico de downloads).
Dashboard, SignalR, Hangfire e expiração automática (limpeza do S3) pertencem a etapas futuras.

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
- **Application** (`FileSharing.Application`): DTOs, validators (FluentValidation), serviços de caso de uso (`AuthService`, `FileUploadService`, `FilePublicLinkService`, `FileDownloadService`) e as abstrações que Infrastructure implementa (`IApplicationDbContext`, `IPasswordHasher`, `IJwtTokenGenerator`, `IFileStorageService`). Não conhece `AmazonS3Client`, `Npgsql` ou qualquer tipo do `Microsoft.AspNetCore.*` — inclusive `Microsoft.AspNetCore.RateLimiting`, que só é referenciado na Api. `FileDownloadService` recebe `ipAddress`/`userAgent` já como `string` simples: toda a extração desses valores de `HttpContext`/headers acontece na Api (`PublicFilesController`), nunca na Application.
- **Infrastructure** (`FileSharing.Infrastructure`): implementações concretas — `ApplicationDbContext` (EF Core/PostgreSQL), `PasswordHasher`/`JwtTokenGenerator` (Identity/JWT), `S3FileStorageService` (AWSSDK.S3, agora também com `CreatePresignedDownloadUrlAsync`).
- **Api** (`FileSharing.Api`): controllers finos, configuração de DI (pasta `Extensions/`), pipeline HTTP, Swagger, rate limiting (`Program.cs`).
- **Mobile** (`FileSharing.Mobile`): cliente .NET MAUI (Android). Conversa com a Api via HTTP/JSON; nunca referencia o AWS SDK nem possui credenciais AWS.

Sem MediatR: cada caso de uso é um serviço de Application chamado diretamente pelo controller (`IAuthService`, `IFileUploadService`, `IFilePublicLinkService`, `IFileDownloadService`), decisão tomada na Etapa 2 e mantida nas etapas seguintes por consistência.

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

## Fluxo do link público (Etapa 4)

```
Client (dono)                 Api                              Public client (qualquer um com o link)
 |                             |                                            |
 |--- POST /api/files/{id}/link ->                                         |
 |    (JWT do dono)            |--- valida ownership/Active/ExpiresAt ---> |
 |                             |--- gera token (RandomTokenGenerator) ---> |
 |                             |--- File.AssignAccessToken(hash(token)) -> |
 |<---- fileId, accessToken, publicUrl (token em texto puro, só aqui) ----|
 |                                                                          |
 |                                          GET /api/public/files/{token} -->|
 |                                          (sem JWT; rate limited)          |
 |                                          |--- hash(token) -----------> |
 |                                          |--- busca File por hash ---> |
 |                                          |<-- Active e não expirado? --|
 |                                          |<---- 200 (dados mínimos) ---|
 |                                          |      ou 404 genérico -------|
```

Pontos importantes:

- **O token nunca é persistido em texto puro.** `File.AccessTokenHash` guarda só o SHA-256 do token (`AccessTokenHasher`); o valor em texto puro existe apenas na resposta HTTP de `POST /api/files/{id}/link` e na memória durante aquela requisição — nunca em log, nunca em outra resposta. Ver `docs/security.md`.
- **`RandomTokenGenerator` é reaproveitado**, não reimplementado — o mesmo gerador criptograficamente seguro já usado para `StorageKey` (Etapa 3) também gera o token público, evitando uma segunda implementação redundante de geração aleatória.
- **A regra "só arquivo `Active` e não expirado pode receber um link" mora em `File.AssignAccessToken` (Domain)**, não só no controller/serviço — `FilePublicLinkService` a checa antes de chamar o método (para devolver `409` em vez de deixar uma exceção vazar), mas a invariante em si é reforçada na entidade.
- **A expiração pública nunca depende do job de limpeza.** `PublicFilesController`/`FilePublicLinkService.GetByAccessTokenAsync` comparam `ExpiresAt` contra `DateTimeOffset.UtcNow` a cada chamada — mesmo que o Hangfire de uma etapa futura ainda não tenha rodado e `Status` continue `Active`, um arquivo cujo prazo já passou é tratado como indisponível imediatamente.
- **Resposta pública uniforme.** Token desconhecido, arquivo expirado e arquivo em qualquer status diferente de `Active` produzem exatamente o mesmo `404` — `PublicFileAccessOutcome` nem carrega um motivo de falha internamente, para que não exista a tentação de vazar essa diferença em uma resposta futura.
- **Gerar o link de novo invalida o anterior.** Como só o hash é guardado, não há "consultar de novo" um token já emitido — cada chamada a `POST /api/files/{id}/link` cria um token novo e sobrescreve `AccessTokenHash`.

## Fluxo de download público (Etapa 5)

```
Public client (qualquer um com o link)          Api                              S3 (LocalStack/AWS)
 |                                                |                                   |
 |--- GET /api/public/files/{token}/download --->|                                   |
 |    (sem JWT; rate limited)                     |--- hash(token) ----------------> |
 |                                                |--- busca File por hash --------> |
 |                                                |--- Active? ExpiresAt > now? ----> |
 |                                                |--- ObjectExistsAsync(StorageKey) -->|
 |                                                |<----------------- existe? --------|
 |                                                |--- registra Download (persiste) ->|
 |                                                |--- CreatePresignedDownloadUrlAsync (GET, curta) |
 |<---- 200 { downloadUrl, expiresAt } ou 404 ---|                                   |
 |                                                                                    |
 |-------------------------- GET downloadUrl (bytes do arquivo) ------------------->|
 |<------------------------------------------------------------------------ 200 ----|
```

O conteúdo do arquivo **nunca** passa pela API neste fluxo, no mesmo espírito do upload — apenas metadata/autorização vão até a API; os bytes trafegam sempre direto entre o cliente público e o S3.

Pontos importantes:

- **Reaproveita a mesma resolução de token da Etapa 4** (`hash(token) → File.AccessTokenHash`), sem nenhuma variação — nunca por `File.Id`. Não existe, e esta etapa não introduziu, nenhuma rota de download que aceite só um `File.Id` como forma de autorização.
- **Ordem das operações é deliberada: nada é persistido antes de tudo estar validado.** Token → `Status == Active` → `ExpiresAt` no futuro → objeto existe no S3 — só depois de todas as quatro passarem é que um `Download` é criado e salvo. Isso garante que **nunca existe um registro de `Download` para uma tentativa que não podia ser atendida** (seção 12 do pedido desta etapa).
- **A presigned URL é gerada depois de persistir o `Download`, de propósito.** `CreatePresignedDownloadUrlAsync` é uma assinatura HMAC local (sem chamada de rede — mesma característica já documentada para `CreatePresignedUploadUrlAsync` em `S3FileStorageService`), então não tem como falhar depois que as validações já passaram. Persistir o registro primeiro garante que uma resposta de sucesso **nunca** é devolvida sem o `Download` correspondente já gravado — a ordem inversa (gerar a URL primeiro) abriria uma janela onde a geração da URL falha e o cliente recebe um erro, mas um registro "fantasma" já teria sido salvo.
- **"Download" = autorizado, não "concluído".** A API não observa a transferência de bytes entre o cliente e o S3 depois de emitir a presigned URL — só sabe que autorizou e emitiu uma. `DownloadedAt` é o instante dessa autorização, não o fim de uma transferência que a API não tem como confirmar.
- **`GET /api/public/files/{token}/download` é uma rota nova, própria — `GET /api/public/files/{token}` (Etapa 4) não foi alterada.** Um cliente ainda pode consultar as informações mínimas do arquivo sem baixar; o download é uma ação explícita e separada.
- **Presigned URL de download é uma abstração nova e distinta da de upload.** `IFileStorageService.CreatePresignedDownloadUrlAsync` (verbo `GET`, expiração própria — `FileStorage:DownloadUrlExpirationSeconds`, 300s por padrão) nunca reaproveita `CreatePresignedUploadUrlAsync` (verbo `PUT`, `FileStorage:PresignedUploadExpirationMinutes`) — são fluxos com propósitos, verbos e janelas de validade diferentes, mesmo compartilhando o mesmo `StorageKey`.
- **Nenhuma alteração de ACL/bucket.** Gerar uma presigned URL não torna o objeto público — é só uma assinatura local; o bucket permanece privado (Public Access Block), como desde a Etapa 3. Ver a nota sobre a limitação do LocalStack Community em `docs/security.md`.
- **Resposta pública uniforme, com mais uma causa colapsada nela.** Além de "token desconhecido"/"expirado"/"status diferente de Active" (já unificados na Etapa 4), esta etapa soma "objeto ausente no S3" ao mesmo `404` genérico — `DownloadFileOutcome` não carrega motivo de falha, assim como `PublicFileAccessOutcome`.
- **IP e User-Agent são extraídos na Api, nunca na Application/Domain.** `PublicFilesController.DownloadPublicFile` lê `HttpContext.Connection.RemoteIpAddress` (não um header — não falsificável por um `X-Forwarded-For` arbitrário, já que esta API não lê esse header nesta etapa) e `Request.Headers.UserAgent` (com fallback `"unknown"` e truncamento em `Download.MaxUserAgentLength`), e só então chama `IFileDownloadService.DownloadAsync(token, ip, userAgent, ct)` com `string`s simples — a Application nunca vê `HttpContext`.
- **Concorrência sem locks.** Duas chamadas simultâneas ao mesmo token produzem dois `Download`s e duas presigned URLs independentes; nenhuma altera `File.Status` — o link continua válido para múltiplos downloads enquanto o arquivo estiver `Active` e não expirado.

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
  - CreatePresignedDownloadUrlAsync   (Etapa 5 — verbo GET, expiração própria e curta)
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
    Common/{Result,FileTypePolicy,RandomTokenGenerator,AccessTokenHasher}.cs
    DTOs/{Auth,Files}/
    Validators/{Auth,Files}/
    Services/Auth/AuthService.cs
    Services/Files/{FileUploadService,FilePublicLinkService,FileDownloadService}.cs
  FileSharing.Infrastructure/
    Persistence/{ApplicationDbContext,Configurations,Migrations}/
    Identity/{PasswordHasher,JwtTokenGenerator}.cs
    Storage/S3FileStorageService.cs
  FileSharing.Api/
    Controllers/{AuthController,FilesController,PublicFilesController}.cs
    Extensions/{Persistence,Auth,Storage,Swagger,ValidationResult,ClaimsPrincipal}Extensions.cs
    RateLimiterPolicyNames.cs
  FileSharing.Mobile/
    Models/UploadableItem.cs
    Services/Api/FileSharingApiClient.cs
    Services/Upload/{IFilePickerService,IFolderPickerService,IFileUploadService,ProgressReportingStream}.cs
    Platforms/Android/{FolderPickerService,ActivityResultBridge}.cs
    ViewModels/UploadViewModel.cs
```
