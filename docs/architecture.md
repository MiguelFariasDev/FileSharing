# Arquitetura

Documenta as decisões arquiteturais até a Etapa 8 (Domain/Database, Autenticação/JWT, Upload de arquivos, Link público de acesso, Download + histórico de downloads, Hangfire + expiração/limpeza automática, SignalR + notificação em tempo real de downloads, Blazor Web/Dashboard).
Um Dashboard administrativo, upload pelo Web e novos mecanismos de autenticação pertencem a etapas futuras (ou estão deliberadamente fora de escopo).

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
- **Application** (`FileSharing.Application`): DTOs, validators (FluentValidation), serviços de caso de uso (`AuthService`, `FileUploadService`, `FilePublicLinkService`, `FileDownloadService`, e agora `FileQueryService` — Etapa 8) e as abstrações que Infrastructure/Api implementam (`IApplicationDbContext`, `IPasswordHasher`, `IJwtTokenGenerator`, `IFileStorageService`, e `IFileDownloadNotifier` — Etapa 7). Não conhece `AmazonS3Client`, `Npgsql` ou qualquer tipo do `Microsoft.AspNetCore.*` — inclusive `Microsoft.AspNetCore.RateLimiting` e `Microsoft.AspNetCore.SignalR`, que só são referenciados na Api. `FileDownloadService` recebe `ipAddress`/`userAgent` já como `string` simples: toda a extração desses valores de `HttpContext`/headers acontece na Api (`PublicFilesController`), nunca na Application. O mesmo vale para a notificação: `IFileDownloadNotifier.NotifyDownloadAsync(ownerUserId, notification, ct)` recebe um `Guid` e um DTO simples — a Application nunca vê `IHubContext`, `Hub` ou qualquer tipo do SignalR.
- **Infrastructure** (`FileSharing.Infrastructure`): implementações concretas — `ApplicationDbContext` (EF Core/PostgreSQL), `PasswordHasher`/`JwtTokenGenerator` (Identity/JWT), `S3FileStorageService` (AWSSDK.S3, agora também com `CreatePresignedDownloadUrlAsync`), e `BackgroundJobs/ExpiredFileCleanupJob` (Hangfire job implementation, Etapa 6).
- **Api** (`FileSharing.Api`): controllers finos, configuração de DI (pasta `Extensions/`), pipeline HTTP, Swagger, rate limiting, e agora `Hubs/` (`NotificationHub`, `SignalRFileDownloadNotifier`, `SubClaimUserIdProvider` — Etapa 7) (`Program.cs`).
- **Mobile** (`FileSharing.Mobile`): cliente .NET MAUI (Android). Conversa com a Api via HTTP/JSON; nunca referencia o AWS SDK nem possui credenciais AWS.
- **Web** (`FileSharing.Web`, Etapa 8): Blazor Server (Interactive Server render mode — já era o modelo do projeto antes desta etapa; não foi migrado para outro). Cliente da Api como qualquer outro — nunca acessa PostgreSQL, EF Core, S3/LocalStack ou Hangfire diretamente; toda comunicação passa por `FileSharingApiClient` (HTTP) e `SignalRNotificationService` (SignalR), ambos em `Services/`. Referencia `FileSharing.Application` apenas para reaproveitar os DTOs de contrato já existentes (`RegisterRequest`, `FileSummaryResponse`, `FileDownloadedNotification` etc.) — nunca `FileSharing.Infrastructure` ou `FileSharing.Api`.

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
- **Resposta pública uniforme.** Token desconhecido, arquivo expirado e arquivo em qualquer status diferente de `Active` produzem exatamente o mesmo `404` — `FilePublicLinkService.GetByAccessTokenAsync` lança sempre a mesma `ResourceNotFoundException(FileErrorCode.NotFound, "Arquivo não disponível.")`, sem um "motivo" interno distinto por causa, para que não exista a tentação de vazar essa diferença em uma resposta futura (ver `docs/api-errors.md`, seção "Anti-enumeração").
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
 |                                                |--- notifica dono via SignalR (best-effort, Etapa 7) |
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
- **Resposta pública uniforme, com mais uma causa colapsada nela.** Além de "token desconhecido"/"expirado"/"status diferente de Active" (já unificados na Etapa 4), esta etapa soma "objeto ausente no S3" ao mesmo `404` genérico — `FileDownloadService.DownloadAsync` lança a mesma `ResourceNotFoundException(FileErrorCode.NotFound, ...)` para as quatro causas, assim como `GetByAccessTokenAsync`.
- **IP e User-Agent são extraídos na Api, nunca na Application/Domain.** `PublicFilesController.DownloadPublicFile` lê `HttpContext.Connection.RemoteIpAddress` (não um header — não falsificável por um `X-Forwarded-For` arbitrário, já que esta API não lê esse header nesta etapa) e `Request.Headers.UserAgent` (com fallback `"unknown"` e truncamento em `Download.MaxUserAgentLength`), e só então chama `IFileDownloadService.DownloadAsync(token, ip, userAgent, ct)` com `string`s simples — a Application nunca vê `HttpContext`.
- **Concorrência sem locks.** Duas chamadas simultâneas ao mesmo token produzem dois `Download`s e duas presigned URLs independentes; nenhuma altera `File.Status` — o link continua válido para múltiplos downloads enquanto o arquivo estiver `Active` e não expirado.

## Hangfire e limpeza de arquivos expirados (Etapa 6)

```
Hangfire Server (recurring "expired-file-cleanup", cron */15 * * * *)
 |
 |--- IExpiredFileCleanupJob.ExecuteAsync() -------------------------------->|
 |                                                                            |
 |     SELECT files WHERE Status = Active AND ExpiresAt <= UtcNow            |
 |     ORDER BY ExpiresAt LIMIT BatchSize (100 por padrão)                   |
 |                                                                            |
 |     para cada File candidato:                                             |
 |         IFileStorageService.DeleteObjectAsync(StorageKey)  ── S3          |
 |         (sucesso, inclusive "já não existia") -> File.MarkAsExpired()     |
 |         SaveChangesAsync()  ── Postgres, por arquivo                      |
 |         (exceção em qualquer um dos dois passos acima) -> loga e         |
 |             segue para o próximo candidato, sem marcar Expired            |
 |                                                                            |
 |<---- ExpiredFileCleanupResult { CandidatesFound, Expired, Failed } -------|
```

**A validade do arquivo é determinada por `ExpiresAt`. O Hangfire é responsável pela limpeza assíncrona do objeto e pela atualização do estado, não pela autorização do download.** Isso não é apenas uma frase de efeito — é uma propriedade verificável do código:

```
                ExpiresAt
                   │
          ┌────────┴────────┐
          ↓                 ↓
       Api pública       Hangfire
   (GetPublicFile,     (ExpiredFileCleanupJob,
    DownloadPublicFile)  a cada ~15 min)
          │                 │
          ↓                 ↓
   compara ExpiresAt    deleta o objeto no S3
   contra UtcNow a      e marca File.Status =
   cada requisição,     Expired — consistência
   independente de      física/lógica, não uma
   Status               checagem de autorização
```

`PublicFilesController`/`FilePublicLinkService`/`FileDownloadService` (Etapas 4/5) já comparavam `ExpiresAt` contra `DateTimeOffset.UtcNow` a cada chamada, sem depender de `Status`, desde antes de o Hangfire existir — essa etapa não alterou uma linha desse caminho. Se o Hangfire ficar uma hora parado (ou falhar indefinidamente), um arquivo cujo `ExpiresAt` já passou continua retornando o mesmo `404` genérico, com `Status` ainda `Active` no banco — os testes de API da Etapa 4/5 (`GetPublicFile_WithExpiredFilesToken_ReturnsNotFound`, `DownloadPublicFile_WithExpiredFilesToken_ReturnsNotFound`) já provam exatamente esse cenário (`ExpiresAt` no passado, `Status` nunca tocado) e continuam passando sem nenhuma mudança.

Pontos importantes:

- **PostgreSQL como storage do Hangfire, reaproveitando o banco existente.** `Hangfire.PostgreSql` cria e migra seu próprio schema `hangfire` (tabelas `hangfire.job`, `hangfire.set`, `hangfire.hash` etc.) na mesma instância/base `filesharing` já usada pelo EF Core — nenhum Redis, nenhum segundo banco. O schema do Hangfire é inteiramente gerenciado pela própria biblioteca (instalado automaticamente no primeiro uso); as migrations do EF Core (`ApplicationDbContext`) não sabem nada sobre ele e vice-versa — schemas isolados na mesma base física.
- **Nenhuma migration do EF Core foi necessária para esta etapa.** `files.ExpiresAt` já tinha um índice desde a `InitialCreate` ("índice, para a futura rotina de expiração" — ver `docs/database.md`), antecipando exatamente este job; o modelo de dados da aplicação não mudou.
- **`IExpiredFileCleanupJob`/`ExpiredFileCleanupJob` vivem em `FileSharing.Infrastructure/BackgroundJobs`**, não em Application — diferente de `FileUploadService`/`FileDownloadService`/`FilePublicLinkService` (que vivem em Application porque são casos de uso de negócio chamados por um controller), este job é puramente infraestrutura de processamento em segundo plano, consistente com a descrição de `Infrastructure` no topo deste documento ("Hangfire job implementations"). Ele só depende de `IApplicationDbContext`/`IFileStorageService`/`IOptions<ExpirationCleanupOptions>` — nenhum tipo do Hangfire aparece no construtor da classe, então o job é testável (e testado) como uma classe simples, sem subir o Hangfire.
- **Os atributos do Hangfire (`[DisableConcurrentExecution]`, `[AutomaticRetry(Attempts = 3)]`) ficam na interface `IExpiredFileCleanupJob.ExecuteAsync`, não na classe.** O pipeline de filtros do Hangfire lê os atributos a partir do `MethodInfo` capturado pela expressão de agendamento (`recurringJobManager.AddOrUpdate<IExpiredFileCleanupJob>(...)`), que aponta para o método da interface — um atributo colocado só na implementação nunca seria visto pelo Hangfire. Isso também os torna inertes quando a classe é chamada diretamente (testes), o que é intencional.
- **Agendamento via `IRecurringJobManager` (injetado), nunca a fachada estática `RecurringJob`.** `services.AddHangfire(...)` registra o storage configurado no container e no `IRecurringJobManager`, mas não necessariamente na propriedade estática `JobStorage.Current` — usar a fachada estática lança `"Current JobStorage instance has not been initialized yet"` mesmo com o storage corretamente configurado (comportamento observado e corrigido durante a validação manual desta etapa). `BackgroundJobsExtensions.UseExpiredFileCleanupSchedule` resolve `IRecurringJobManager` via `app.Services` — a própria recomendação do Hangfire para aplicações ASP.NET Core.
- **Nenhum Dashboard exposto.** `app.UseHangfireDashboard()` nunca é chamado nesta etapa — expor `/hangfire` sem um filtro de autorização é um problema de segurança conhecido (dashboard mostra jobs, argumentos, stack traces de falhas) e ficou fora de escopo deliberadamente. Ver `docs/security.md`.
- **`Status == Active` é o filtro primário da query, não uma checagem redundante.** Um `File` `PendingUpload` nunca tem `ExpiresAt` (nulo — `NULL <= qualquer_data` nunca é verdadeiro em SQL) e um já `Expired` simplesmente não volta a ser candidato; nenhum dos dois precisa de tratamento especial no código do job. `File.MarkAsExpired()` (Domain) também lança se `Status != Active`, funcionando como uma segunda barreira caso a query algum dia tivesse um bug.
- **Idempotência via o próprio comportamento do S3, não uma checagem extra.** `DeleteObject` no S3 real (e no LocalStack, replicando o mesmo comportamento — verificado empiricamente nesta etapa) **não lança erro** ao apagar uma chave que já não existe; "objeto ainda lá" e "objeto já removido por uma execução anterior parcial" chegam ao job exatamente da mesma forma (sucesso), sem precisar de um `ObjectExistsAsync` prévio.
- **Cada arquivo é salvo individualmente, imediatamente após seu próprio `DeleteObjectAsync` ter sucesso — nunca dentro de uma transação que abrange a chamada ao S3.** O S3 não participa de uma transação do PostgreSQL; manter uma transação aberta durante a chamada de rede só alargaria a janela de inconsistência sem nenhum ganho. Se o `SaveChangesAsync` de um arquivo falhar depois do objeto já ter sido removido do S3, a linha continua `Active` no banco — a próxima execução tenta excluir de novo (sucesso automático, pela idempotência do S3) e marca `Expired` então: uma janela de inconsistência documentada e autocurável, nunca permanente.
- **Falha de um arquivo nunca aborta o lote.** Cada candidato é processado dentro do seu próprio `try/catch`; uma exceção (do `DeleteObjectAsync` ou do `SaveChangesAsync`) incrementa o contador de falhas e loga com o `FileId` para correlação — nunca com o nome original do arquivo, token ou URL assinada — e o `File` permanece `Active`, elegível para uma execução futura. Os demais candidatos do mesmo lote continuam sendo processados normalmente.
- **Batching com `Take(BatchSize)`, sem paginação por `OFFSET`.** Uma única execução nunca carrega mais que `ExpirationCleanup:BatchSize` (100 por padrão) linhas em memória. Um backlog maior que isso **não** é drenado inteiramente em uma única execução — ele se esgota ao longo de execuções recorrentes sucessivas (~15 em 15 minutos), o que é aceitável porque a limpeza é eventual e não é a barreira de segurança (essa é sempre a checagem de `ExpiresAt` na Api, independente do job).
- **Concorrência via o lock distribuído do próprio Hangfire (`[DisableConcurrentExecution(timeoutInSeconds: 600)]`), nunca um lock in-process/global.** O lock é armazenado no mesmo PostgreSQL usado como storage, então funciona corretamente mesmo com múltiplas instâncias da aplicação rodando `HangfireServer` simultaneamente — apenas uma execução do `expired-file-cleanup` roda por vez em todo o cluster. O timeout de 600s limita apenas quanto tempo uma segunda tentativa de início espera pelo lock, não a duração da execução em si.
- **Retry limitado a 3 tentativas (`[AutomaticRetry(Attempts = 3)]`), abaixo do padrão de 10 do Hangfire.** Como uma falha de um único arquivo nunca escapa do `try/catch` interno (vira uma contagem em `Failed`, não uma exceção), uma exceção que escapa do método inteiro só pode significar algo sistêmico (ex.: o banco ou a própria query em lote indisponíveis) — vale tentar de novo algumas vezes, mas não indefinidamente; se as tentativas se esgotarem, a próxima execução recorrente (~15 min depois) pega os mesmos candidatos de novo de qualquer forma.
- **Registrado automaticamente no startup, com id estável (`"expired-file-cleanup"`).** `RecurringJobManager.AddOrUpdate` é ele mesmo idempotente nesse id — reiniciar a aplicação nunca duplica o job recorrente, apenas garante que ele existe com a configuração (cron, método) atual.
- **`File` expirado sem `StorageKey`: cenário inexistente pelo modelo atual, verificado, não inventado.** `File.StorageKey` é obrigatório e validado no construtor (`ArgumentException` se nulo/vazio) e nunca é limpo depois — não existe caminho, nem hipotético, para um `File` (em qualquer `Status`) ter `StorageKey` nulo/vazio. Por isso o job não tem (nem precisa de) um branch especial para esse caso.
- **Ambiente de teste sem Hangfire real.** `AddBackgroundJobs`/`UseExpiredFileCleanupSchedule` só registram storage/server/agendamento quando há uma connection string `Postgres` configurada **e** `ExpirationCleanup:Enabled = true`; `FileSharing.ApiTests` roda em ambiente `"Testing"` com banco InMemory e nunca configura uma connection string real, então o guard já pula toda a parte de Hangfire sem precisar de nenhuma mudança em `CustomWebApplicationFactory` — `IExpiredFileCleanupJob` continua registrado no container (então é resolvível), só nunca agendado. `FileSharing.UnitTests`/`FileSharing.IntegrationTests` nunca passam pelo Hangfire de forma alguma — instanciam `ExpiredFileCleanupJob` diretamente, como uma classe simples (mesmo padrão de `FileDownloadServiceTests`/`FileDownloadIntegrationTests`).

## SignalR e notificação em tempo real de downloads (Etapa 7)

```
Owner (autenticado)                    Api                              Downloader (link público)
 |                                      |                                       |
 |-- HubConnection (JWT) ------------->|                                       |
 |   /hubs/notifications                |                                       |
 |   [Authorize] — rejeita sem JWT      |                                       |
 |<-- conectado ------------------------|                                       |
 |                                      |                                       |
 |                                      |<-- GET /api/public/files/{token}/download --|
 |                                      |    (fluxo da Etapa 5, inalterado:     |
 |                                      |     valida token/Status/ExpiresAt/S3, |
 |                                      |     registra Download, gera URL)      |
 |                                      |                                       |
 |                                      |-- IFileDownloadNotifier.NotifyDownloadAsync(ownerUserId, ...) |
 |                                      |     (só depois do Download já persistido e da URL já emitida) |
 |                                      |-- Clients.User(ownerUserId).SendAsync("FileDownloaded", ...) |
 |<-- "FileDownloaded" { fileId,        |                                       |
 |     originalFileName, downloadedAt } |                                       |
 |                                      |-- 200 { downloadUrl, expiresAt } ou 404 (best-effort acima não afeta isto) --> |
```

Pontos importantes:

- **O fluxo de download da Etapa 5 não foi alterado** — mesma ordem de validação (token → `Status` → `ExpiresAt` → objeto no S3), mesma exceção genérica para toda falha, mesmo registro de `Download`. Esta etapa apenas adiciona um passo **depois** de tudo isso já ter tido sucesso: notificar o dono. Nenhum endpoint novo, nenhuma mudança de contrato em `GET /api/public/files/{token}/download`.
- **`IFileDownloadNotifier` é a única abstração nova em Application** (`FileSharing.Application.Abstractions.Notifications`), no mesmo espírito de `IFileStorageService`/`IApplicationDbContext` — a Application conhece só a interface (`NotifyDownloadAsync(Guid ownerUserId, FileDownloadedNotification, CancellationToken)`), nunca `Hub`, `IHubContext` ou qualquer tipo `Microsoft.AspNetCore.SignalR.*`.
- **A implementação concreta (`SignalRFileDownloadNotifier`) vive em `FileSharing.Api/Hubs`, não em Infrastructure** — ela depende de `IHubContext<NotificationHub>`, e `NotificationHub` precisa estar na Api para ser mapeado por `Program.cs` (`app.MapHub<NotificationHub>(...)`). Mesma categoria de decisão já documentada no `CLAUDE.md` ("Api ... SignalR hub registration").
- **Identidade da conexão vem só do claim `sub` do JWT, nunca de algo que o cliente envie.** `SubClaimUserIdProvider` (`IUserIdProvider`) é necessário porque o JWT desta API usa `MapInboundClaims = false` (Etapa 2) — os claims mantêm o nome original ("sub"), não são remapeados para `ClaimTypes.NameIdentifier`, que é o que o `IUserIdProvider` padrão do SignalR usa. Sem esse provider customizado, `Context.UserIdentifier` seria sempre `null` e `Clients.User(...)` não alcançaria ninguém. O provider reaproveita `ClaimsPrincipalExtensions.TryGetUserId` — a mesma lógica de extração de identidade já usada por todo endpoint REST — em vez de duplicá-la.
- **`Clients.User(ownerUserId.ToString())`, nunca `Clients.All`/`Clients.AllExcept`/grupos escolhidos pelo cliente.** O isolamento entre usuários não é uma checagem adicional em algum lugar — é uma propriedade estrutural do mecanismo escolhido: o SignalR só entrega a quem tem aquele `UserIdentifier` específico. `File B` sendo baixado nunca aparece para o dono de `File A`, e vice-versa (testado ponta a ponta com duas conexões reais — ver `tests/FileSharing.ApiTests/Notifications/NotificationHubTests.cs`).
- **Múltiplas conexões do mesmo usuário são responsabilidade do próprio SignalR** — `Clients.User(id)` já entrega a todas as conexões daquele `UserIdentifier` (várias abas, dispositivos etc.); nenhum `Dictionary<UserId, ConnectionId>` próprio foi criado.
- **A notificação é best-effort, por design — este é o requisito mais crítico desta etapa.** `FileDownloadService.DownloadAsync` chama `IFileDownloadNotifier.NotifyDownloadAsync` só depois do `Download` já persistido e da presigned URL já emitida, envolvendo a chamada em um `try/catch` que descarta qualquer exceção — uma falha do SignalR (Hub indisponível, erro de transporte, timeout) nunca reverte o `Download`, nunca invalida a URL já gerada, e nunca faz o endpoint público responder algo diferente de `200`. `SignalRFileDownloadNotifier` também captura e loga suas próprias falhas internamente (`LogWarning`) — o `try/catch` em `FileDownloadService` é um segundo cinto de segurança, não o único.
- **Download inválido/expirado nunca notifica ninguém.** A chamada ao notifier só existe depois de todas as validações da Etapa 5 já terem passado — um token desconhecido, expirado, ou um objeto ausente no S3 retornam o mesmo `404` de sempre, sem nunca alcançar o notifier (testado explicitamente).
- **Payload mínimo, deliberadamente.** `FileDownloadedNotification { FileId, OriginalFileName, DownloadedAt }` — nunca `AccessToken`, `AccessTokenHash`, presigned URL, `StorageKey`, IP ou User-Agent do downloader (esses dois últimos continuam só no histórico `Download`, Etapa 5, e nunca saem por SignalR). Um teste de regressão (`FileDownloadedNotification_ExposesOnlyFileIdOriginalFileNameAndDownloadedAt`) garante que o payload nunca seja ampliado silenciosamente.
- **Hub sem lógica de negócio.** `NotificationHub` não define nenhum método invocável pelo cliente, não consulta o banco, não sabe o que é um `File` — é só um ponto de conexão autenticado; toda a decisão de "quem notificar, com o quê" acontece em `FileDownloadService`/`SignalRFileDownloadNotifier`.
- **JWT sobre query string, mas só para o Hub.** Um navegador não consegue anexar um cabeçalho `Authorization` a um upgrade de WebSocket (nem a uma requisição SSE) — por isso o cliente JS do SignalR envia o token como `?access_token=...`. `AuthExtensions.AddJwtAuthentication` ganhou um `JwtBearerEvents.OnMessageReceived` que só aceita esse parâmetro quando o caminho começa com `/hubs/notifications` (`HubEndpoints.Notifications`); fora dali, o comportamento é exatamente o mesmo de antes desta etapa — o REST continua exigindo o header `Authorization`, e um token na query string de um endpoint REST é ignorado (verificado manualmente: `GET /api/auth/me?access_token=...` sem o header continua `401`).
- **Rate limiting inalterado.** `PublicFilesController` continua com a mesma política `public-files` da Etapa 4/5; o Hub não tem, e não precisa de, uma política de rate limit própria — ele não é uma superfície de adivinhação de token como o link público.
- **Escalabilidade: funciona em uma única instância; múltiplas instâncias em produção exigirão um backplane.** O SignalR aqui usa o armazenamento de conexões em memória padrão (nenhum backplane Redis/Azure SignalR foi adicionado nesta etapa, conforme pedido). Isso significa que **hoje**, com uma única instância da Api rodando, tudo funciona corretamente — inclusive múltiplas conexões do mesmo usuário. Se uma implantação futura rodar **múltiplas instâncias** da Api atrás de um load balancer (ex.: AWS ECS Fargate com várias tasks), uma conexão SignalR estabelecida com a instância A não é visível pela instância B — um download processado pela instância B não conseguiria notificar um dono cuja conexão está na instância A. Resolver isso exigirá um backplane (ex.: Redis, ou Azure SignalR Service) — uma decisão de infraestrutura de deployment/escala, não uma mudança na segurança do fluxo atual (o isolamento por `Clients.User` continua correto independentemente do backplane escolhido). Ver também `docs/deployment.md`.

## FileSharing.Web — Blazor Server (Etapa 8)

```
Browser (SignalR circuit do Blazor Server)
     |
     v
FileSharing.Web (Program.cs)
     |
     +-- Login.razor / Register.razor --> FileSharingApiClient --> POST /api/auth/{login,register} (Api)
     |
     +-- Dashboard.razor -------------> FileSharingApiClient --> GET /api/files/mine
     |         |                                              --> GET /api/files/{id}/downloads
     |         |                                              --> POST /api/files/{id}/link
     |         |
     |         +-- SignalRNotificationService --> HubConnection --> /hubs/notifications (Api, Etapa 7)
     |
     +-- MainLayout.razor (Sair) -----> AuthTokenProvider.Clear() + SignalRNotificationService.StopAsync()
```

O Web nunca acessa PostgreSQL, EF Core, S3/LocalStack ou Hangfire diretamente — toda comunicação passa pelos contratos HTTP/SignalR já existentes da Api (Etapas 1–7), inalterados por esta etapa, com exceção de dois endpoints mínimos novos (`GET /api/files/mine`, `GET /api/files/{id}/downloads`) que faltavam para o dashboard funcionar — ver `docs/api.md`.

### Por que Blazor Server foi mantido (não migrado)

O projeto já usava Blazor Server (`AddInteractiveServerComponents`/`AddInteractiveServerRenderMode`, template "Blazor Web App" do .NET 8+) antes desta etapa — essa configuração foi verificada em `src/FileSharing.Web/Program.cs` e mantida sem alteração de modelo de hospedagem. Páginas que precisam de interatividade (`Login`, `Register`, `Home`, `Dashboard`) declaram `@rendermode @(new InteractiveServerRenderMode(prerender: false))` explicitamente, desabilitando o pré-render estático — necessário porque o estado de autenticação (`AuthTokenProvider`/`ApiAuthenticationStateProvider`, ambos `Scoped` por circuito) só existe depois que o circuito interativo real é estabelecido; com pré-render habilitado, a primeira passagem estática rodaria com um escopo `Scoped` transitório e vazio, antes de qualquer login.

### Onde e como o JWT é armazenado

O JWT emitido por `POST /api/auth/login` fica **inteiramente em memória, no servidor**, dentro de `AuthTokenProvider` (`Scoped`) — nunca chega ao navegador, nunca é colocado em `localStorage`/`sessionStorage`/cookie, nunca aparece em uma URL. Isso é possível justamente porque a aplicação já é Blazor **Server**: como o C# roda inteiramente no servidor, o token nunca *precisa* atravessar a rede até o navegador, ao contrário de um cliente Blazor WebAssembly (que rodaria no browser e não teria escolha). `ApiAuthenticationStateProvider` guarda apenas o `ClaimsPrincipal` derivado de `GET /api/auth/me` (Id/Email) para alimentar `<AuthorizeView>`/`<AuthorizeRouteView>` — nunca decodifica o JWT localmente para extrair claims (evita adicionar uma dependência de parsing de JWT só para reler dois campos que a Api já expõe por um endpoint testado).

**Trade-off documentado, deliberado:** como não há nenhum armazenamento no navegador, um F5 forçado (recarregamento completo da página) cria um novo circuito Blazor Server — e, com ele, uma nova instância `Scoped` de `AuthTokenProvider`, vazia. Ou seja, **a sessão não sobrevive a um F5 forçado**; o usuário precisa logar novamente. Uma reconexão transitória do circuito (o `ReconnectModal` já existente no template, para uma queda breve de rede) reutiliza o **mesmo** circuito e não perde esse estado. Essa escolha prioriza segurança (superfície de exposição do token reduzida ao mínimo possível) sobre a conveniência de sobreviver a um F5 — dado que nenhuma fase pediu um mecanismo de persistência de sessão no navegador, e introduzir um (cookie, localStorage) seria, por si só, uma escolha de arquitetura de autenticação nova, fora do escopo desta etapa.

### FileSharingApiClient — cliente HTTP centralizado

Único ponto do projeto que fala com a Api. Responsabilidades: anexar `Authorization: Bearer <token>` **somente** em chamadas autenticadas (nunca em `login`/`register`); mapear todo `HttpStatusCode` de erro para um `ApiErrorType` (`Unauthorized`, `Forbidden`, `NotFound`, `Conflict`, `TooManyRequests`, `ValidationFailed`, `ServerError`, `Network`) com uma mensagem de usuário genérica — nunca o corpo bruto da resposta (que poderia ser uma página de exceção com stack trace, SQL, erro da AWS etc.); emitir o evento `SessionExpired` em qualquer `401`, tratado uma única vez por `Components/Shared/SessionGuard.razor` (monta em `MainLayout`, faz logout + redireciona para `/login`) — nenhum componente individual precisa saber lidar com sessão expirada.

### Dashboard

`GET /api/files/mine` alimenta a lista; cada linha mostra nome, tipo, tamanho (formatado), status (badge com **texto**, nunca só cor — acessibilidade), data de criação, tempo restante (calculado no cliente a partir de `ExpiresAt`, só para exibição — nunca usado para autorizar nada; ver seção seguinte), quantidade de downloads e a célula de link público (`Components/Shared/FileLinkCell.razor`). Estados tratados explicitamente: carregando, erro (com botão de retentar), vazio ("Você ainda não possui arquivos... envie pelo aplicativo móvel"), sem nenhum botão de upload falso — upload continua sendo responsabilidade exclusiva do Mobile (Etapa 3).

**Tempo restante é só cosmético.** `RemainingTimeLabel`/`IsEffectivelyExpired` comparam `ExpiresAt` contra `DateTimeOffset.UtcNow` **no navegador/servidor Blazor**, só para decidir o texto exibido — nunca chamam a Api para autorizar nada com base nisso. Se o relógio do cliente achar que um arquivo "quase expirado" ainda está ativo, ou vice-versa, a Api continua sendo a única fonte de verdade: `POST /api/files/{id}/link` e `GET /api/public/files/{token}/download` fazem sua própria checagem de `ExpiresAt`/`Status`, independentemente do que o dashboard mostra. Quando o Hangfire (Etapa 6) eventualmente marcar um arquivo como `Expired`, o dashboard só reflete isso na próxima leitura de `GetMyFilesAsync` (carregamento inicial ou clique em "Atualizar") — não há polling para isso, de propósito (ver "Performance" abaixo).

**Link público:** o Web nunca gera token nem hash — `Components/Shared/FileLinkCell.razor` só exibe o que `POST /api/files/{id}/link` (Etapa 4, inalterado) devolve. "Gerar link"/"Gerar novo link" chamam exatamente o mesmo endpoint; a única diferença é que regenerar exige confirmação explícita antes ("Gerar um novo link invalidará o link atual. Deseja continuar?"), porque o token de uma chamada anterior nunca pode ser recuperado de novo (só o hash é persistido). O link retornado (texto puro) fica só na memória do componente naquela sessão — nunca é salvo em nenhum outro lugar pelo Web.

**Histórico de downloads:** `GET /api/files/{id}/downloads` (Etapa 8, novo — ver `docs/api.md`) é carregado sob demanda, ao expandir "Ver histórico de downloads" por arquivo — nunca eagerly para todos os arquivos da lista. Mostra só data/hora; IP e User-Agent (existentes no banco desde a Etapa 5) **não são exibidos** nesta etapa, por decisão deliberada de escopo (ver `docs/security.md`).

### SignalR no cliente Web

`Services/Notifications/SignalRNotificationService` é o único lugar do projeto que conhece `HubConnection` — `Dashboard.razor` só assina o evento `FileDownloaded` (tipado com o mesmo `FileSharing.Application.DTOs.Notifications.FileDownloadedNotification` da Etapa 7, nenhum contrato duplicado) e o evento `StateChanged` (para o indicador discreto de conexão). Conecta a `{Api:BaseUrl}/hubs/notifications` usando o JWT da sessão (`AuthTokenProvider.AccessToken`, lido a cada tentativa de conexão/reconexão, nunca capturado uma única vez) via `AccessTokenProvider` do `HubConnectionBuilder`.

**Reconexão automática, obrigatória por esta etapa:** `.WithAutomaticReconnect([TimeSpan.Zero, 2s, 10s, 30s])` — tenta imediatamente, depois em 2s/10s/30s, e então desiste, ficando em `Disconnected` até uma ação explícita (não existe um laço manual de reconexão). O status (`Conectado`/`Conectando.../Reconectando.../Offline`) aparece como um texto pequeno e discreto na barra superior — nunca um elemento visual dominante.

**Nunca duas conexões simultâneas para o mesmo usuário nesta aba:** `StartAsync` é idempotente — se já existe uma conexão (`_connection is not null`), a chamada não faz nada. `Dashboard.OnInitializedAsync` chama `StartAsync` sem aguardá-lo (`_ = NotificationService.StartAsync();`) deliberadamente — conectar ao Hub de notificações nunca deve atrasar o carregamento da lista de arquivos nem o login, já que `StartAsync` já trata e loga suas próprias falhas internamente (ver Etapa 7).

**Atualização em tempo real, sem recarregar nada.** Ao receber `FileDownloaded`: (1) mostra um toast não bloqueante (`ToastService`/`Components/Shared/ToastContainer.razor` — nunca `alert()` do navegador); (2) incrementa **apenas** o contador de downloads do arquivo afetado (`file.Summary with { DownloadCount = ... + 1 }`), localizado por `FileId` na lista já carregada; (3) se o histórico daquele arquivo específico já estiver expandido na tela, recarrega só o histórico **dele** (`GetDownloadHistoryAsync`, uma chamada); nunca um `GetMyFilesAsync` completo disparado pela notificação. Se o arquivo não estiver na tela (lista desatualizada), nada é forçado.

### Logout

`MainLayout.LogoutAsync`: (1) `SignalRNotificationService.StopAsync()` — encerra a conexão autenticada antes de mais nada, para que ela nunca sobreviva à sessão; (2) `AuthTokenProvider.Clear()`; (3) `ApiAuthenticationStateProvider.MarkUserAsLoggedOut()`; (4) `NavigationManager.NavigateTo("/login")`. Nessa ordem — nunca um logout "só visual" que deixe a conexão SignalR ou o token ainda válidos em memória.

### Autorização de página e o papel do `IAuthenticationService` da Api pipeline

`Dashboard.razor` carrega `[Authorize]`; `Routes.razor` usa `<AuthorizeRouteView>` com `<NotAuthorized><RedirectToLogin /></NotAuthorized>`. Isso cobre dois casos distintos:

- **Circuito interativo já autenticado** (ex.: navegação client-side depois do login): a checagem acontece inteiramente na árvore de componentes Blazor, contra `ApiAuthenticationStateProvider` — nenhuma pergunta à Api nesse momento.
- **Primeira requisição estática (pré-circuito)** a uma URL protegida (ex.: um usuário não autenticado digita `/dashboard` direto no navegador): `[Authorize]` no componente também anexa metadado de autorização ao *endpoint* HTTP subjacente, que o roteamento do ASP.NET Core aplica via `AuthorizationMiddleware` **mesmo sem uma chamada explícita a `UseAuthorization()`** — e o caminho de falha desse middleware chama `ChallengeAsync`, que exige um `IAuthenticationService`/esquema de autenticação registrado, ou a requisição retorna `500` (bug real, encontrado e corrigido durante a validação manual desta etapa — ver `docs/security.md`). Por isso `Program.cs` registra um esquema de cookie **só para servir de alvo de challenge/redirect** (`LoginPath = "/login"`) — nenhum código deste projeto jamais chama `SignInAsync`, então nenhum cookie de autenticação real chega a ser emitido; a identidade de fato usada em toda a aplicação continua sendo exclusivamente `ApiAuthenticationStateProvider`, alimentada pelo JWT da Api.

### Performance

Nenhum polling periódico de `GET /api/files/mine` — a única forma de atualização automática é o evento `FileDownloaded` via SignalR. Um timer local (`System.Threading.Timer`, 30 em 30 segundos) só re-renderiza o texto de "tempo restante" — nunca faz nenhuma chamada de rede.

## FileSharing.Mobile — .NET MAUI Android (Etapa 13)

Documentação completa em `docs/mobile.md` (fluxo de upload/S3, autenticação, SecureStorage, SignalR, configuração, segurança, limitações). Resumo arquitetural: o projeto Mobile foi dividido em dois — `FileSharing.Mobile.Core` (`net10.0` puro: ViewModels, cliente HTTP, sessão de autenticação, orquestração de upload, cliente SignalR — nada de `Microsoft.Maui.*`/`Android.*` direto) e `FileSharing.Mobile` (a "head" `net10.0-android`: as implementações concretas de SecureStorage/FilePicker/Storage-Access-Framework/Clipboard/Share, e todo o XAML). Motivo: neste ambiente, `FileSharing.Mobile` só compila para `net10.0-android`, e um projeto de teste `net10.0` não pode referenciar um projeto `net10.0-android` — a separação é o que torna `tests/FileSharing.Mobile.Tests` executável via `dotnet test` sem emulador/dispositivo.

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

## Observability & Diagnostics (Etapa 12)

Política de dados sensíveis nos logs: ver `docs/security.md`. Aqui só a mecânica.

### Correlation ID

```
CorrelationIdMiddleware (Api/Middleware/, primeiro middleware da pipeline)
  → lê X-Correlation-ID do request (reaproveita se válido: ≤128 chars, [A-Za-z0-9_-], único valor)
  → gera Guid.NewGuid() caso contrário
  → HttpContext.Items["CorrelationId"]        (lido via HttpContext.GetCorrelationId())
  → Response.Headers["X-Correlation-ID"]      (TryAdd — antes de next(), sobrevive a exceção)
  → _logger.BeginScope("CorrelationId: {CorrelationId}", id)   (acompanha todo log da requisição)
```

`GlobalExceptionHandler` reanexa o header explicitamente porque `ExceptionHandlerMiddleware` reseta a resposta (limpando headers já setados) antes de invocá-lo — `HttpContext.Items` sobrevive a esse reset, o header por si só não. `AddProblemDetails(options => options.CustomizeProblemDetails = ...)` (`Program.cs`) grava `correlationId` em **todo** `ProblemDetails`, não só nos de exceção não tratada.

### Logging estruturado

`Microsoft.Extensions.Logging` (já usado desde etapas anteriores — nada de Serilog nesta etapa). Eventos relevantes, todos com propriedades estruturadas (`{FileId}`, `{UserId}`, nunca concatenação de string), nunca dado sensível (ver `docs/security.md`):

```
AuthService            : registro/login bem-sucedido ou rejeitado (Information/Warning)
FileUploadService      : upload iniciado/concluído/rejeitado, com duração
FilePublicLinkService  : link gerado/rejeitado; acesso público concedido/negado
FileDownloadService    : download concedido/negado, com duração
S3FileStorageService   : Debug apenas — detalhe de baixo nível, nunca duplica o que a
                          camada Application já logou em Information/Warning
ExpiredFileCleanupJob  : início, candidatos encontrados, resultado final (já existia desde a
                          Etapa 6; only a métrica foi adicionada nesta etapa)
SignalRFileDownloadNotifier : notificação enviada/falhou, com duração (já existia desde a
                          Etapa 7; duração e métrica adicionadas nesta etapa)
GlobalExceptionHandler : exceção não tratada, com CorrelationId
```

Console local formatado via `Logging:Console:FormatterName: "simple"` + `FormatterOptions.IncludeScopes: true` (`appsettings.json`) — sem essas duas chaves explícitas, o provedor de console usa um modo legado que ignora silenciosamente `IncludeScopes`, então o scope do correlation id nunca aparece (achado real desta etapa).

### Health Checks

```
GET /health/live    tag "live"   — "self" apenas, sem dependência externa (nunca falha por Postgres/S3 estarem fora)
GET /health/ready   tag "ready"  — PostgresHealthCheck + S3HealthCheck
```

- `PostgresHealthCheck`: uma query real e barata (`Users.Select(u => u.Id).Take(1)`) via `IApplicationDbContext` — não `Database.CanConnectAsync()`, porque esse método se comporta de forma diferente entre o provider Npgsql (produção) e o InMemory (`FileSharing.ApiTests`); uma query real funciona identicamente nos dois.
- `S3HealthCheck`: `IFileStorageService.GetObjectMetadataAsync` contra uma chave que nunca existirá (`__healthcheck__/probe`) — um "não encontrado" limpo já prova que o storage está alcançável; nunca `PutObject`/`DeleteObject`, nunca upload real. Passa pela mesma abstração que `FileUploadService`/`FileDownloadService` já usam, então herda automaticamente o endpoint certo (LocalStack em Development, S3 real em produção) sem nenhuma configuração própria — e, em `FileSharing.ApiTests`, herda o mock já existente (nunca uma chamada de rede real durante os testes).
- Resposta: o texto padrão do framework (`"Healthy"`/`"Unhealthy"`) — nenhum `ResponseWriter` customizado que serializaria exceção/connection string na resposta.

### Métricas

`FileSharing.Application.Observability.AppMetrics` (`System.Diagnostics.Metrics`, nativo do .NET desde a 6, nenhum pacote NuGet novo). Contadores (`uploads.initiated/completed`, `downloads`, `public_links.generated/accessed`, `auth.attempts`, `files.expired`, `storage.objects_deleted`, `cleanup.failures`, `signalr.notifications`) e histogramas de duração (upload/download/cleanup), todos sob o Meter `"FileSharing.Application"`. Tags limitadas a `operation`/`result` — nunca `UserId`/`FileId`/token/IP como tag (cardinalidade alta seria o oposto do objetivo). Observável hoje via `dotnet-counters monitor --process-id <pid> FileSharing.Application`, sem exportador configurado — ver `docs/security.md` para a decisão de não adicionar o SDK do OpenTelemetry nesta etapa.

### Request logging

`Microsoft.AspNetCore.HttpLogging` (built-in do framework) — método, path, protocolo, status, duração. Nunca headers (`Authorization`/`Cookie` inclusos) nem corpo de request/response. `PublicFilesController` está inteiramente fora dele (`[HttpLogging(HttpLoggingFields.None)]`) porque sua própria rota contém o token público — ver `docs/security.md`. Liga/desliga via `Observability:EnableRequestLogging` (padrão `true`).

## Recuperação de senha e tratamento de erros

### Pipeline de erro

```text
Serviço (AuthService/PasswordResetService)
      │  throw new AuthenticationException(AuthErrorCode.InvalidCredentials, "...")
      ▼
GlobalExceptionHandler.TryHandleAsync
      │  exception is AppException appException?
      │    sim → status/título vêm da própria exceção, log em nível Information
      │    não → status 500 fixo, título genérico, log em nível Error (exceção completa)
      ▼
IProblemDetailsService.TryWriteAsync
      │  CustomizeProblemDetails (ErrorHandlingExtensions) estampa:
      │    - correlationId (sempre)
      │    - code: AppException.PublicCode, ou "VALIDATION_ERROR" (ValidationProblemDetails),
      │      ou "INTERNAL_ERROR" (qualquer outro caso)
      ▼
Resposta HTTP { type, title, status, correlationId, traceId, code, errors? }
```

`RateLimiterPolicyNames.*` (rate limiting) é a única exceção a este fluxo — a rejeição acontece na pipeline de middleware antes do `GlobalExceptionHandler`, então `RateLimitingExtensions.AddApiRateLimiting`'s `OnRejected` escreve o mesmo formato de resposta diretamente, sem passar por `IExceptionHandler`.

Ver `docs/api-errors.md` para o catálogo completo de `code`s e `docs/security.md` para a política de logging (uma `AppException` nunca é `LogError`).

### Fluxo de recuperação de senha

```text
POST /api/auth/forgot-password { email }
      │
      ▼
PasswordResetService.ForgotPasswordAsync
      │  usuário existe? ──não──► retorna normalmente (nenhum token, nenhum e-mail)
      │  sim
      ▼
Invalida tokens ainda válidos do usuário (PasswordResetToken.Invalidate)
      │
      ▼
Gera token (RandomTokenGenerator) → hash (AccessTokenHasher) → persiste PasswordResetToken
      │
      ▼
IEmailService.SendPasswordResetEmailAsync(email, resetLink)   ← link para o Web (PasswordReset:WebResetUrlBase)
      │
      ▼
Controller sempre responde 202 com a mesma mensagem (exista ou não a conta)


GET /api/auth/reset-password/{token}          POST /api/auth/reset-password { token, newPassword }
      │                                              │
      ▼                                              ▼
PasswordResetService.ValidateResetTokenAsync   PasswordResetService.ResetPasswordAsync
      │                                              │
      └──────────────┬───────────────────────────────┘
                      ▼
      FindValidTokenOrThrowAsync (hash do token → busca por igualdade)
         não encontrado → DomainException(PasswordResetInvalid, 404)
         já usado        → DomainException(PasswordResetUsed, 410)
         expirado         → DomainException(PasswordResetExpired, 410)
         válido            → segue (200 na validação; troca a senha + marca usado no reset)
```

- **Por que uma entidade própria, e não reaproveitar `File.AccessTokenHash`:** são conceitos diferentes (sessão de reset de um `User` vs. link público de um `File`), com ciclos de vida e regras de expiração/uso próprios — misturar os dois acoplaria duas features sem relação, só por semelhança superficial do mecanismo de hash.
- **Por que `AccessTokenHasher` é reaproveitado mesmo assim:** o *algoritmo* (SHA-256 determinístico para permitir busca por igualdade) é genuinamente o mesmo problema técnico já resolvido — reaproveitar a função evita uma segunda implementação redundante do mesmo hash, sem acoplar as duas entidades entre si (cada uma tem sua própria coluna/tabela).
- **Por que `AuthService`/`PasswordResetService` são serviços separados:** mesmo padrão já usado em `FileSharing.Application.Services.Files` (um serviço por caso de uso coeso: upload, link público, download, query) — `AuthService` continua só com registro/login/usuário atual; a lógica de recuperação de senha (token, e-mail, invalidação) fica isolada em `PasswordResetService`, injetado separadamente em `AuthController`.

## Diagrama de pastas do backend

```
src/
  FileSharing.Domain/
    Entities/{User,File,Download,PasswordResetToken}.cs
    Enums/{FileStatus,CompressionType}.cs
  FileSharing.Application/
    Abstractions/{Persistence,Security,Storage,Notifications,Email}/
    Common/{FileTypePolicy,RandomTokenGenerator,AccessTokenHasher}.cs
    Common/Errors/{AuthErrorCode,FileErrorCode,AuthorizationErrorCode,ValidationErrorCode,ResourceErrorCode,SystemErrorCode,RateLimitErrorCode,ErrorCodeCatalog}.cs   # error-code padronização
    Common/Exceptions/{AppException,AuthenticationException,ForbiddenException,ResourceNotFoundException,ConflictException,DomainException}.cs                      # error-code padronização — único mecanismo de erro de negócio; Common/Result.cs e os *Outcome (CompleteUploadOutcome/GenerateLinkOutcome/DownloadFileOutcome/PublicFileAccessOutcome) foram removidos nesta fase
    Observability/AppMetrics.cs                                      # Etapa 12
    DTOs/{Auth,Files,Notifications}/
    Validators/{Auth,Files}/
    Services/Auth/{AuthService,PasswordResetService}.cs
    Services/Files/{FileUploadService,FilePublicLinkService,FileDownloadService,FileQueryService}.cs
  FileSharing.Infrastructure/
    Persistence/{ApplicationDbContext,Configurations,Migrations}/     # inclui PasswordResetTokenConfiguration
    Identity/{PasswordHasher,JwtTokenGenerator}.cs
    Storage/S3FileStorageService.cs
    Email/DevelopmentEmailService.cs                                  # error-code padronização — única implementação de IEmailService desta fase
    BackgroundJobs/{ExpiredFileCleanupJob,ExpirationCleanupOptions}.cs
  FileSharing.Api/
    Controllers/{AuthController,FilesController,PublicFilesController}.cs   # AuthController ganhou forgot-password/reset-password
    Hubs/{NotificationHub,SubClaimUserIdProvider,SignalRFileDownloadNotifier,HubEndpoints}.cs
    Extensions/{Persistence,Auth,Storage,BackgroundJobs,Notifications,Swagger,ValidationResult,ClaimsPrincipal,Observability,ErrorHandling,RequestLogging,RateLimiting,ServiceRegistration}Extensions.cs
                                                                       # ServiceRegistrationExtensions agrega os demais em 3 chamadas (AddAuthenticationServices/AddApplicationServices/AddCrossCuttingServices) — Program.cs fica só com essas 3 linhas, sem comentários de agrupamento
    Middleware/{GlobalExceptionHandler,CorrelationIdMiddleware}.cs   # GlobalExceptionHandler agora também trata AppException, não só o caminho 500
    HealthChecks/{PostgresHealthCheck,S3HealthCheck}.cs              # Etapa 12
    Options/ObservabilityOptions.cs                                  # Etapa 12
    RateLimiterPolicyNames.cs                                        # + PasswordReset
  FileSharing.Web/                      # Blazor Server (Etapa 8)
    Extensions/{Authentication,ApiClient,AppServices,ServiceRegistration}Extensions.cs   # Program.cs fica só com AddApplicationServices(configuration)
    Models/{ApiSettings,ApiResult,ApiErrorType,PublicLinkResponse,ToastMessage}.cs        # ApiResult ganhou Code; ApiErrorType ganhou Gone
    Services/{AuthTokenProvider,ApiAuthenticationStateProvider,FileSharingApiClient,ToastService}.cs   # + ForgotPasswordAsync/ValidateResetTokenAsync/ResetPasswordAsync
    Services/Notifications/{SignalRNotificationService,NotificationConnectionState}.cs
    Components/Pages/{Login,Register,ForgotPassword,ResetPassword,Home,Dashboard}.razor
    Components/Layout/{MainLayout,AuthLayout}.razor
    Components/Shared/{FileLinkCell,ToastContainer,ConnectionStatus,SessionGuard,RedirectToLogin}.razor
    wwwroot/js/interop.js               # clipboard only — o JWT nunca chega ao JS
  FileSharing.Mobile.Core/               # .NET MAUI Android (Etapa 13) — ver docs/mobile.md
    Models/{UploadableItem,ApiResult,PublicLinkResponse,UploadStage}.cs   # ApiResult ganhou Code; ApiErrorType ganhou Gone
    Services/ApiClient/{FileSharingApiClient,ApiClientOptions}.cs        # + ForgotPasswordAsync/ValidateResetTokenAsync/ResetPasswordAsync
    Services/Authentication/AuthSession.cs
    Services/Upload/{FileUploadService,S3UploadHttpClient,ProgressReportingStream,interfaces}.cs
    Services/SignalR/{SignalRNotificationService,NotificationConnectionState,interfaces}.cs
    Services/Platform/{INavigationService,IClipboardService,IShareService,IMainThreadDispatcher}.cs
    Services/Storage/ISecureStorageService.cs
    ViewModels/{Login,Register,ForgotPassword,ResetPassword,Home,Upload,FileDetails,History,FileItem}ViewModel.cs
  FileSharing.Mobile/                    # net10.0-android head project — MAUI/Android-specific only
    Extensions/{Authentication,ApiClient,Upload,Notification,Platform,ViewModel,View,ServiceRegistration}Extensions.cs   # MauiProgram.cs fica só com 3 chamadas
    Services/Storage/SecureStorageService.cs        # only place touching Microsoft.Maui.Storage.SecureStorage
    Services/Upload/FilePickerService.cs            # only place touching Microsoft.Maui.Storage.FilePicker
    Services/Platform/{Navigation,Clipboard,Share,MainThreadDispatcher}Service.cs
    Platforms/Android/{FolderPickerService,ActivityResultBridge}.cs
    Views/{Login,Register,ForgotPassword,ResetPassword,Home,Upload,FileDetails,History}Page.xaml
    Components/{FileCard,StatusBadge}.xaml
    Converters/{IconKeyToEmojiConverter,StatusLabelToKindConverter,StringToBoolConverter,InvertedBoolConverter}.cs
    Resources/Raw/appsettings.json                  # Api:BaseUrl / Api:HubUrl — never a secret
    Resources/Styles/{Colors,Styles}.xaml            # blue/glass theme; GlassCardBorder substitui o antigo componente GlassCard (ver histórico do projeto)
    MauiProgram.cs, App.xaml.cs, AppShell.xaml.cs
```
