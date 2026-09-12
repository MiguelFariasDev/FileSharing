# Segurança

Documentação da segurança implementada até a Etapa 6 (Autenticação/JWT + Upload de arquivos + Link público de acesso + Download + histórico de downloads + Hangfire/expiração automática). Notificações (SignalR) e um Dashboard autenticado serão documentados nas etapas correspondentes.

---

## Hashing de senha

- Senhas nunca são armazenadas em texto puro — apenas o hash é persistido, na coluna `PasswordHash` de `User`.
- O hashing é feito por `IPasswordHasher` (`FileSharing.Application.Abstractions.Security`), implementado em `FileSharing.Infrastructure.Identity.PasswordHasher` usando `Microsoft.AspNetCore.Identity.PasswordHasher<TUser>` — nenhum algoritmo criptográfico é implementado manualmente.
- A senha original enviada pelo cliente só existe em memória durante a requisição (`RegisterRequest.Password` / `LoginRequest.Password`) e nunca é logada, persistida ou retornada em qualquer resposta.

## JWT

- Gerado por `IJwtTokenGenerator` (`FileSharing.Application.Abstractions.Security`), implementado em `FileSharing.Infrastructure.Identity.JwtTokenGenerator` usando `System.IdentityModel.Tokens.Jwt`.
- Algoritmo de assinatura: HMAC-SHA256 (`SecurityAlgorithms.HmacSha256`), com `SymmetricSecurityKey` derivada de `Jwt:SecretKey`.
- Claims incluídas:
  - `sub`: `User.Id`
  - `email`: `User.Email`
  - `jti`: identificador único do token (`Guid.NewGuid()`), previne replay/reemissão idêntica
  - `iat`: momento de emissão (Unix time, UTC)
  - `exp`: momento de expiração (`iat + Jwt:ExpirationMinutes`)
- Expiração padrão: 60 minutos (`Jwt:ExpirationMinutes`), configurável, mas sempre curta — não é criado refresh token nesta etapa.
- O token **nunca** contém dados de arquivos, chaves de armazenamento ou qualquer informação sensível além do necessário para identificar o usuário.
- O JWT (nem o `SecretKey`) é logado em nenhum ponto do sistema.

## Validação do token (API)

Configurada em `FileSharing.Api.Extensions.AuthExtensions.AddJwtAuthentication`, via `Microsoft.AspNetCore.Authentication.JwtBearer`:

- `ValidateIssuer` + `ValidIssuer` (`Jwt:Issuer`)
- `ValidateAudience` + `ValidAudience` (`Jwt:Audience`)
- `ValidateIssuerSigningKey` + `IssuerSigningKey` (derivada de `Jwt:SecretKey`)
- `ValidateLifetime` (respeita o `exp`)
- `ClockSkew` reduzido para 1 minuto (em vez do padrão de 5 minutos), já que os tokens têm vida curta.
- `MapInboundClaims = false`, para que o claim `sub` chegue ao `ClaimsPrincipal` com seu nome original (sem ser remapeado para `ClaimTypes.NameIdentifier`), permitindo que `GET /api/auth/me` leia o claim `sub` diretamente para identificar o usuário — **nunca** um id enviado pelo cliente.

O pipeline HTTP aplica `UseAuthentication()` antes de `UseAuthorization()`.

## Gestão de segredos

- `Jwt:SecretKey` **não** possui valor real em `appsettings.json`/`appsettings.Development.json` (campo vazio) — a aplicação falha explicitamente na inicialização se o valor não for configurado.
- Em desenvolvimento, o segredo é fornecido via **User Secrets** (`dotnet user-secrets set "Jwt:SecretKey" "..."` no projeto `FileSharing.Api`), que fica fora do repositório.
- Em produção, o valor deve vir de uma variável de ambiente ou do AWS Secrets Manager (conforme `CLAUDE.md`), nunca do código-fonte ou de arquivos versionados.
- Exemplo de configuração (sem segredo real):
  ```json
  "Jwt": {
    "Issuer": "FileSharing",
    "Audience": "FileSharing.Api",
    "SecretKey": "<configure-via-user-secrets-ou-variavel-de-ambiente>",
    "ExpirationMinutes": 60
  }
  ```

## Erros de autenticação genéricos

- `POST /api/auth/login` retorna sempre a mesma mensagem — `"Credenciais inválidas."` — tanto para e-mail inexistente quanto para senha incorreta, com o mesmo código HTTP (`401`), para não revelar se um e-mail está ou não cadastrado.
- `GET /api/auth/me` retorna `401` genérico tanto para ausência de token quanto para token inválido/expirado.

## Upload de arquivos e armazenamento (S3)

- **Bucket privado.** `filesharing-dev` (LocalStack) é criado com Public Access Block habilitado (`infrastructure/docker/localstack-init/01-create-bucket.sh`); em produção, o bucket real deve manter a mesma configuração — nunca tornar o bucket ou objetos públicos.
- **O conteúdo do arquivo nunca passa pela API.** `POST /api/files/upload` recebe só metadata e devolve uma presigned URL; o MAUI envia os bytes direto ao S3 via `PUT`. A API nunca vê, armazena ou faz proxy do conteúdo.
- **Presigned URL de vida curta** (`FileStorage:PresignedUploadExpirationMinutes`, 15 min por padrão) — não confundir com a janela de 24h do arquivo (`File.ExpiresAt`), que só começa a contar depois do `complete`, nunca da emissão da URL.
- **`StorageKey` é opaco e não previsível** (`RandomTokenGenerator`, `RandomNumberGenerator` — nunca `Guid.NewGuid()` truncado, nunca sequencial), e nunca deriva do nome original do arquivo (`OriginalFileName != StorageKey`).
- **Nada é confiado sem verificação:** nome do arquivo, Content-Type e tamanho declarados pelo cliente no `initiate` são validados contra `FileTypePolicy`/`FileStorageOptions.MaxFileSizeBytes` (`InitiateUploadRequestValidator`); no `complete`, o tamanho e o Content-Type são reconfirmados contra o **objeto real no S3** — se algo não bater, o arquivo nunca vira `Active`.
- **Ownership sempre verificado.** `POST /api/files/{id}/complete` só age sobre um `File` que pertence ao usuário do JWT; "não existe" e "pertence a outro usuário" retornam a mesma resposta (`404`), sem diferenciação.
- **Allowlist explícita de tipos** (`FileTypePolicy`): documentos (PDF, EPUB), imagens (JPEG/PNG/WebP/GIF), vídeo (MP4/WebM/MOV/MKV), áudio (MP3/WAV/OGG/M4A/AAC/FLAC) e ZIP (pastas). Sem executáveis, sem scripts, sem allowlist "genérica" — extensão e Content-Type devem ser consistentes entre si.
- **Sem Base64 em nenhum ponto do fluxo** — o conteúdo trafega como bytes crus no corpo do `PUT`; Base64 só aumentaria o tamanho transferido sem qualquer ganho.
- **Arquivos individuais nunca são recomprimidos.** Só pastas passam por compressão (ZIP, lossless, feito no cliente MAUI antes do upload) — a Api/Infrastructure não decodifica nem recodifica nenhum arquivo.
- **Credenciais AWS nunca chegam ao MAUI.** O app mobile só conhece a presigned URL recebida da API; não existe (e não deveria existir) `AWSSDK.*` no projeto `FileSharing.Mobile`.
- **Segredos AWS seguem o mesmo padrão do `Jwt:SecretKey`:** em desenvolvimento, `AWS:AccessKey`/`AWS:SecretKey` em `appsettings.Development.json` são credenciais dummy do LocalStack (`test`/`test`, aceitas apenas por ele) — nunca credenciais reais. Em produção, a AWS Access Key/Secret Key não devem existir em arquivo algum: usar IAM Role (ECS Task Role) ou AWS Secrets Manager.
- **Nada de presigned URL, token ou credencial em log.** Os serviços de upload (`FileUploadService`, `S3FileStorageService`) não logam a URL pré-assinada nem qualquer segredo — apenas dados não sensíveis (ex.: `FileId`) seriam candidatos a log em uma etapa futura de observabilidade.

## Link público de acesso (token, hash e validação)

- **Geração do token.** `POST /api/files/{id}/link` (`FileSharing.Application.Services.Files.FilePublicLinkService.GenerateLinkAsync`) gera o token público reutilizando `RandomTokenGenerator` — o mesmo gerador criptograficamente seguro (`RandomNumberGenerator`) já usado para `StorageKey` — em vez de criar uma segunda implementação redundante. Saída: 256 bits de entropia (32 bytes aleatórios, base64url), bem acima do piso de 128 bits/22+ caracteres exigido. Nunca é derivado de `File.Id`, de `OriginalFileName` ou de qualquer outro dado previsível, e cada chamada gera um token novo e independente — nenhum token é reaproveitado.
- **Nunca em texto puro no banco.** Só o hash (`File.AccessTokenHash`, SHA-256 em hexadecimal, 64 caracteres — `FileSharing.Application.Common.AccessTokenHasher`) é persistido. SHA-256 puro (sem sal, sem custo computacional deliberado como `PasswordHasher`) é a escolha certa aqui — e não a mesma técnica usada para senha — porque o token precisa ser **localizado por igualdade de hash** (`GET /api/public/files/{token}` calcula o hash do token recebido e busca `WHERE AccessTokenHash = @hash`); um hash de senha salgado não permitiria essa busca determinística. Isso é seguro porque o próprio token já carrega 256 bits de entropia — ele não é uma senha memorizável sujeita a dicionário, então não precisa do custo computacional de um KDF lento.
- **O token original só existe na resposta de criação.** `GenerateLinkResponse.AccessToken` (texto puro) é devolvido **apenas** na resposta de `POST /api/files/{id}/link`; o hash nunca é devolvido em nenhuma resposta. Como só o hash fica persistido, não existe como "recuperar" um token depois — chamar o endpoint de novo gera um token novo e substitui o hash anterior (o link antigo passa a não resolver mais nada).
- **Nunca aparece em log.** Nem `FilePublicLinkService` nem os controllers (`FilesController.GenerateLink`, `PublicFilesController.GetPublicFile`) logam o token, o hash ou qualquer parte deles — o mesmo princípio já aplicado a senha/JWT/presigned URL.
- **Geração só quando arquiteturalmente válido.** `File.AssignAccessToken` (Domain) só aceita associar um hash a um `File` com `Status == Active` e `!IsExpired(...)` — chamado a partir de `Status == PendingUpload` ou depois de expirado lança `InvalidOperationException`. `FilePublicLinkService.GenerateLinkAsync` checa a mesma regra antes de chamar o método de domínio, para devolver `409 Conflict` em vez de deixar a exceção vazar — mas a invariante em si mora no `File`, não no controller/serviço (defesa em profundidade).
- **Ownership sempre verificado.** `POST /api/files/{id}/link` só age sobre um `File` que pertence ao usuário do JWT; "não existe" e "pertence a outro usuário" retornam a mesma resposta (`404`), sem diferenciação — mesmo padrão já usado em `complete`.
- **Resposta pública genérica por construção.** `PublicFileAccessOutcome` (`FileSharing.Application.Services.Files`) não carrega motivo de falha nenhum — só `IsSuccess`/`Value`. Token nunca emitido, arquivo expirado (`IsExpired()`, checado **imediatamente** contra `DateTimeOffset.UtcNow`, sem depender de nenhum job de limpeza), e arquivo em qualquer status diferente de `Active` colapsam no mesmo `NotAvailable()` → `404 Not Found` sem corpo, em `PublicFilesController`. Não há como um atacante inferir, pela resposta, se um determinado arquivo já existiu.
- **Índice único em `AccessTokenHash`** (`FileConfiguration`, ver `docs/database.md`) garante unicidade a nível de banco — dois arquivos nunca podem compartilhar o mesmo hash (e, por extensão, o mesmo token).
- **Rate limiting em `GET /api/public/files/{token}` e `GET /api/public/files/{token}/download`.** Configurado em `Program.cs` via `Microsoft.AspNetCore.RateLimiting` (`AddRateLimiter`/`AddFixedWindowLimiter`, política `public-files`, aplicada a `PublicFilesController` inteiro com `[EnableRateLimiting]` no nível da classe): janela fixa de 1 minuto, 30 requisições permitidas, excedente rejeitado com `429 Too Many Requests`. **Os dois endpoints compartilham o mesmo contador** — não é uma política nova por endpoint, é deliberadamente a mesma proteção da Etapa 4 reaproveitada, porque ambos expõem exatamente a mesma superfície de ataque (adivinhação de token; o de download não introduz um vetor diferente que justificasse uma política separada). 256 bits de entropia já torna a enumeração inviável computacionalmente; o rate limiting é uma camada adicional, não a principal defesa. Particionamento por IP/cliente não foi implementado nesta etapa — pode ser adicionado depois sem alterar os endpoints.
- **Nenhum endpoint de listagem/busca pública.** Não existe rota que liste arquivos, tokens, links ou downloads — o único jeito de "descobrir" um `File` publicamente é já possuir o token exato.

## Download público e histórico de downloads (Etapa 5)

- **Mesma estratégia de token da Etapa 4, sem alteração.** `GET /api/public/files/{token}/download` (`FileSharing.Application.Services.Files.FileDownloadService.DownloadAsync`) calcula o hash do token recebido (`AccessTokenHasher.Hash`, o mesmo SHA-256 determinístico) e localiza o `File` por `AccessTokenHash` — nunca por `File.Id`. Não existe (e este endpoint deliberadamente não introduz) nenhuma rota de download baseada só em `File.Id`; um teste de API garante isso (`PublicFileDownloadEndpointsTests.DownloadPublicFile_ThereIsNoRouteToDownloadByFileIdAlone`).
- **Resposta pública genérica, com mais uma causa colapsada nela.** `DownloadFileOutcome` segue o mesmo desenho de `PublicFileAccessOutcome` — só `IsSuccess`/`Value`, sem motivo de falha. Token desconhecido, `Status != Active`, `ExpiresAt` já atingido (checado imediatamente contra `DateTimeOffset.UtcNow`, sem depender do job de limpeza da Etapa 6 — ver seção "Hangfire" abaixo) e **objeto ausente no S3** (`IFileStorageService.ObjectExistsAsync`) resultam todos no mesmo `404 Not Found` de `PublicFilesController.DownloadPublicFile`. A API nunca responde algo como "objeto não encontrado no S3" — isso vazaria um detalhe de infraestrutura (e distinguiria "token válido mas storage inconsistente" de "token inválido").
- **Presigned URL de download, nunca a de upload.** `IFileStorageService.CreatePresignedDownloadUrlAsync` (nova abstração, implementada em `S3FileStorageService` com `HttpVerb.GET`) é um método **distinto** de `CreatePresignedUploadUrlAsync` — verbo diferente (`GET` vs `PUT`), expiração configurada por uma opção independente (`FileStorage:DownloadUrlExpirationSeconds`, 300s por padrão — nunca os minutos do upload, nunca as 24h de `File.ExpiresAt`). Um teste unitário garante que o fluxo de download nunca chama `CreatePresignedUploadUrlAsync` (`FileDownloadServiceTests.DownloadAsync_RequestsAPresignedDownloadUrl_NeverAnUploadUrl`).
- **Nenhuma mudança de ACL/política do bucket.** Gerar uma presigned URL (upload ou download) é só uma assinatura HMAC local — nenhuma chamada de API ao S3 para tornar o objeto ou o bucket público, nem aqui nem em nenhum outro ponto do código. O bucket continua com Public Access Block habilitado (`infrastructure/docker/localstack-init/01-create-bucket.sh`), inalterado desde a Etapa 3.
- **"Download" = autorizado/emitido, não "concluído".** A API não tem visibilidade sobre se o cliente de fato terminou de transferir os bytes depois de receber a presigned URL — só sabe que emitiu uma para quem apresentou um token válido. Por isso `Download` é registrado no momento em que **todas** as checagens (token, `Status`, `ExpiresAt`, existência no S3) já passaram, nunca antes — nunca existe um registro de `Download` para uma tentativa rejeitada. Ver `docs/architecture.md` para o raciocínio completo.
- **IP do cliente: `HttpContext.Connection.RemoteIpAddress`, nunca um header.** `PublicFilesController.DownloadPublicFile` lê o endereço IP do peer TCP da conexão — não de `X-Forwarded-For` ou qualquer outro cabeçalho controlável pelo cliente, que esta API não lê nesta etapa. Isso significa que um cliente não consegue falsificar o IP registrado apenas enviando um header arbitrário. Atrás de um proxy reverso/ALB (etapa de deployment), `RemoteIpAddress` passaria a refletir o endereço do proxy — corrigir isso exigiria configurar o Forwarded Headers Middleware do ASP.NET Core corretamente (validando `KnownProxies`/`KnownNetworks`), o que **não** foi feito nesta etapa (não havia proxy configurado antes, e adicioná-lo sem necessidade seria uma mudança estrutural fora de escopo). Compatível com IPv4 e IPv6 por construção, já que vem de `System.Net.IPAddress`; a coluna `downloads.IpAddress` (`varchar(45)`) já comporta a representação canônica mais longa de IPv6.
- **User-Agent: `Request.Headers.UserAgent`, com fallback e truncamento explícitos.** Ausência do cabeçalho nunca derruba a requisição — vira o literal `"unknown"` (constante em `PublicFilesController`). Um valor mais longo que `Download.MaxUserAgentLength` (1000, a mesma constante que `DownloadConfiguration` usa para `HasMaxLength`) é truncado explicitamente antes de chegar ao Domain/Application — evitando tanto uma exceção do `Download` (que exige um valor não vazio) quanto um erro do Postgres por exceder o `varchar(1000)`. Nenhum outro cabeçalho é lido ou persistido.
- **Nada de token/hash/presigned URL em log.** Nem `FileDownloadService` nem `PublicFilesController.DownloadPublicFile` logam o token recebido, o `AccessTokenHash` calculado, ou a presigned URL gerada (ela carrega parâmetros de assinatura — `X-Amz-Signature`, `X-Amz-Credential` — que nunca devem aparecer em log). Mesmo princípio já aplicado a senha/JWT/link público desde etapas anteriores.
- **Concorrência.** Nenhum lock é usado. Duas chamadas simultâneas ao mesmo token geram dois registros de `Download` independentes e duas presigned URLs independentes; nenhuma delas altera `File.Status` — o mesmo link pode ser usado múltiplas vezes enquanto o arquivo permanecer válido. Não há estado compartilhado mutável em memória entre requisições (cada chamada é uma transação própria contra o Postgres).

## Hangfire e limpeza automática de arquivos expirados (Etapa 6)

- **A autorização de download nunca depende do Hangfire.** `GetPublicFile`/`DownloadPublicFile` (Etapas 4/5) comparam `ExpiresAt` contra `DateTimeOffset.UtcNow` a cada chamada, independente de `Status` — essa checagem existia antes do Hangfire e não foi alterada nesta etapa. Se o job ficar parado, atrasado ou falhando indefinidamente, um arquivo com `ExpiresAt` no passado continua retornando o mesmo `404` genérico; os testes de API já existentes (`GetPublicFile_WithExpiredFilesToken_ReturnsNotFound`, `DownloadPublicFile_WithExpiredFilesToken_ReturnsNotFound` — ambos com `Status` deliberadamente deixado `Active`) comprovam exatamente isso. Ver `docs/architecture.md` para o diagrama completo.
- **Nenhum Dashboard exposto.** `app.UseHangfireDashboard()` não é chamado em nenhum lugar do código desta etapa — `/hangfire` não existe como rota. Um dashboard sem filtro de autorização exporia detalhes de execução (argumentos de job, stack traces de falhas, contagens) a qualquer visitante; adicioná-lo corretamente autenticado fica para uma etapa futura.
- **PostgreSQL reaproveitado como storage do Hangfire, sem credenciais novas.** `Hangfire.PostgreSql` usa a mesma connection string `ConnectionStrings:Postgres` já usada pelo EF Core — nenhum segredo novo foi introduzido; a gestão de segredos segue exatamente o padrão já documentado acima para `Jwt:SecretKey`/AWS.
- **Nada de token/hash/presigned URL em log pelo job.** `ExpiredFileCleanupJob` só loga `FileId` (para correlação) e a exceção técnica quando uma exclusão falha — nunca o `OriginalFileName`, nunca um token ou hash (o job nem os enxerga), nunca uma URL assinada.
- **Falha de um arquivo nunca é engolida silenciosamente, mas também nunca derruba o processamento dos demais.** Cada candidato é processado dentro do seu próprio `try/catch`; uma exceção é logada com `LogError` (nível de erro, visível em qualquer sink configurado) e conta para `ExpiredFileCleanupResult.Failed` — o arquivo problemático simplesmente permanece `Active` para uma tentativa futura, sem impedir os demais candidatos do mesmo lote.
- **Nenhuma alteração de ACL/bucket pelo job.** `ExpiredFileCleanupJob` só chama `IFileStorageService.DeleteObjectAsync` (a mesma abstração já existente, não uma nova chamada direta ao AWS SDK) — nenhuma API de ACL/política de bucket é invocada; o bucket permanece privado como desde a Etapa 3.
- **Rate limiting dos endpoints públicos inalterado.** Esta etapa não toca em `PublicFilesController`, `RateLimiterPolicyNames` nem na configuração de `AddRateLimiter` em `Program.cs` — a política `public-files` (janela de 1 minuto, 30 requisições) continua protegendo `GET /api/public/files/{token}` e `GET /api/public/files/{token}/download` exatamente como na Etapa 5.
- **Concorrência seguida via lock distribuído do Hangfire, não um lock próprio da aplicação.** `[DisableConcurrentExecution(timeoutInSeconds: 600)]` (na interface `IExpiredFileCleanupJob`, ver `docs/architecture.md`) garante que só uma execução do `expired-file-cleanup` roda por vez em todo o cluster (mesmo com múltiplas instâncias da API), sem introduzir um mecanismo de lock adicional.

### Por que a API falha sem configuração

`Jwt:SecretKey` vazio e `ConnectionStrings:Postgres` ausente já faziam a API recusar-se a subir corretamente (Etapa 2). O mesmo princípio se aplica ao storage: sem `FileStorage:BucketName`/`AWS:ServiceURL` configurados, as chamadas ao S3 simplesmente falham (o cliente tenta o AWS real, sem credenciais/bucket válidos) em vez de silenciosamente fingir sucesso — nunca há um "modo mock" implícito em produção.

## Outras práticas aplicadas

- Todas as datas são tratadas em UTC (`DateTimeOffset.UtcNow`), nunca `DateTime.Now`.
- `RegisterAsync` trata `DbUpdateException` (violação da constraint única de `Email`) como cadastro duplicado, cobrindo condições de corrida entre a checagem de existência e a inserção.
- `IApplicationDbContext` mantém a Application layer livre de referência direta a `Npgsql`/EF Core de infraestrutura; a implementação concreta (`ApplicationDbContext`) fica em `FileSharing.Infrastructure`.

### Limitação conhecida do LocalStack Community (dev only)

Durante a Etapa 5 foi verificado, empiricamente, que o LocalStack Community (imagem gratuita usada em `docker-compose.yml`) **não** aplica de fato o Block Public Access/políticas de bucket contra requisições anônimas — uma requisição `GET` sem assinatura contra o objeto no bucket local respondeu com sucesso, apesar de `infrastructure/docker/localstack-init/01-create-bucket.sh` configurar o bloqueio. Isso é uma limitação da emulação S3 gratuita do LocalStack (recursos de IAM/controle de acesso refinado são um recurso do LocalStack Pro), não do código desta aplicação: nenhum ponto do fluxo de upload/link/download chama qualquer API de ACL ou política de bucket — `CreatePresignedUploadUrlAsync`/`CreatePresignedDownloadUrlAsync` são só uma assinatura HMAC local. Em AWS real, com Block Public Access habilitado e sem ACL pública nos objetos (exatamente a configuração que o script já aplica), uma requisição anônima seria rejeitada normalmente. Por esse motivo, o teste de integração que tentaria comprovar isso contra o LocalStack foi removido — ver o comentário em `tests/FileSharing.IntegrationTests/Files/FileDownloadIntegrationTests.cs`.
