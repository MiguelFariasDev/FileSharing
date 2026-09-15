# Catálogo de erros

Todo erro que esta API retorna carrega um `code` estável (ver `docs/api.md`'s "Formato padronizado de erro"). Esta página documenta apenas os códigos **efetivamente implementados** — nenhum código fictício.

## Arquitetura

```text
Business Logic
      ↓
Enum interno fortemente tipado (AuthErrorCode, FileErrorCode, ...)
      ↓
Exceção personalizada (AuthenticationException, ConflictException, DomainException, ...)
      ↓
GlobalExceptionHandler
      ↓
Mapeamento explícito (ErrorCodeCatalog) — nunca `enum.ToString().ToUpperInvariant()`
      ↓
Código público estável (string)
      ↓
Resposta HTTP ({ code, title, status, correlationId, traceId })
      ↓
Mobile / Web
```

Os enums internos (`AuthErrorCode.InvalidCredentials`, etc.) vivem em `FileSharing.Application.Common.Errors` e **nunca** são serializados diretamente — o nome de um membro do enum pode mudar no futuro sem quebrar nenhum cliente, porque o contrato público é sempre a string do `ErrorCodeCatalog`, não `nameof(...)`/`ToString()`. O HTTP status também nunca fica dentro do enum — é decidido pela exceção que o carrega (`AuthenticationException` → 401, `ConflictException` → 409, etc.), permitindo o mesmo código de negócio (ex.: `AuthErrorCode.EmailAlreadyExists`) ser usado por diferentes exceções/contextos HTTP no futuro sem acoplar a regra de negócio ao protocolo.

Erros inesperados (uma exceção que não é uma das acima — EF Core, AWS SDK, um bug) nunca chegam ao cliente com detalhe algum: sempre viram `INTERNAL_ERROR` com uma mensagem genérica; o tipo/mensagem/stack trace reais só existem no log do servidor.

## Autenticação (`AuthErrorCode`)

| Enum Value              | Código Público                  | HTTP | Descrição                                      |
| ------------------------ | -------------------------------- | ---: | ----------------------------------------------- |
| `InvalidCredentials`     | `AUTH_INVALID_CREDENTIALS`       |  401 | E-mail ou senha incorretos, ou usuário inexistente (mesma resposta para ambos) |
| `EmailAlreadyExists`     | `AUTH_EMAIL_ALREADY_EXISTS`      |  409 | E-mail já cadastrado |
| `InvalidEmail`           | `AUTH_EMAIL_INVALID`             |    — | Reservado — hoje coberto por `VALIDATION_ERROR` (FluentValidation) |
| `AccountDisabled`        | `AUTH_ACCOUNT_DISABLED`          |    — | Reservado — não há conceito de conta desabilitada implementado ainda |
| `PasswordResetInvalid`   | `AUTH_PASSWORD_RESET_INVALID`    |  404 | Token de recuperação nunca existiu |
| `PasswordResetExpired`   | `AUTH_PASSWORD_RESET_EXPIRED`    |  410 | Token de recuperação expirou |
| `PasswordResetUsed`      | `AUTH_PASSWORD_RESET_USED`       |  410 | Token de recuperação já foi utilizado |
| `PasswordResetRateLimited` | `AUTH_PASSWORD_RESET_RATE_LIMITED` | 429 | Reservado — hoje o limite de `forgot-password` responde com o `RATE_LIMITED` genérico (mesma política de todo rate limiting desta API), não com este código específico |

## Autorização (`AuthorizationErrorCode`)

| Enum Value  | Código Público   | HTTP | Descrição |
| ----------- | ----------------- | ---: | --------- |
| `Forbidden` | `AUTH_FORBIDDEN`  |  403 | Reservado — nenhuma rota atual distingue "autenticado mas sem permissão" de "não autenticado"; todas as checagens de propriedade hoje (arquivo de outro usuário, etc.) respondem `404` genérico, não `403`, para não revelar a existência do recurso (ver `docs/security.md`) |

## Arquivos (`FileErrorCode`)

| Enum Value            | Código Público               | HTTP | Descrição |
| ---------------------- | ------------------------------ | ---: | --------- |
| `NotFound`             | `FILE_NOT_FOUND`               |  404 | Arquivo inexistente, pertencente a outro usuário (`FilesController`), ou token público inexistente/expirado/removido (`PublicFilesController` — mesmo código para as três causas, ver "Anti-enumeração" abaixo) |
| `Expired`              | `FILE_EXPIRED`                  |  409 | Geração de link para um arquivo já expirado (`GenerateLinkAsync`) |
| `AccessDenied`         | `FILE_ACCESS_DENIED`           |    — | Reservado |
| `InvalidType`          | `FILE_INVALID_TYPE`            |    — | Reservado — hoje coberto por `VALIDATION_ERROR` |
| `InvalidUploadState`   | `FILE_UPLOAD_INVALID_STATE`    |  409 | `CompleteUploadAsync`: upload não pendente, objeto ausente no storage, ou tamanho/Content-Type divergente do declarado (mesmo código público para as quatro causas — a mensagem interna, não o código, distingue o motivo) |
| `InvalidFileName`      | `FILE_INVALID_NAME`            |    — | Reservado — hoje coberto por `VALIDATION_ERROR` |
| `UploadNotCompleted`   | `FILE_UPLOAD_NOT_COMPLETED`    |  409 | Geração de link para um arquivo cujo upload ainda não foi concluído (`GenerateLinkAsync`) |

### Anti-enumeração nas rotas públicas

`PublicFilesController.GetPublicFile`/`DownloadPublicFile` (sem autenticação) lançam sempre a mesma exceção — `ResourceNotFoundException(FileErrorCode.NotFound, "Arquivo não disponível.")` — para token inexistente, arquivo expirado, arquivo removido, ou objeto ausente no S3. O código, a mensagem e o status HTTP são idênticos nos quatro casos; só a mensagem *interna* (nunca exposta) e o log (nunca com o token) variam. Isso preserva a garantia de segurança que já existia antes desta fase — ver `PublicFilesEndpointsTests.GetPublicFile_UnknownAndExpiredTokens_ReturnTheSameGenericBody` e `PublicFileDownloadEndpointsTests.DownloadPublicFile_UnknownAndExpiredTokens_ReturnTheSameGenericBody`.

## Validação (`ValidationErrorCode`)

| Enum Value       | Código Público      | HTTP | Descrição |
| ----------------- | --------------------- | ---: | --------- |
| `InvalidRequest`  | `VALIDATION_ERROR`    |  400 | Um ou mais campos inválidos (FluentValidation) — a resposta inclui `errors` (dicionário campo → mensagens) além de `code`/`title` |

## Recursos genéricos (`ResourceErrorCode`)

| Enum Value  | Código Público         | HTTP | Descrição |
| ----------- | ------------------------ | ---: | --------- |
| `NotFound`  | `RESOURCE_NOT_FOUND`    |    — | Reservado — disponível para uso fora do domínio de auth/arquivos |
| `Conflict`  | `RESOURCE_CONFLICT`     |    — | Reservado |

## Sistema (`SystemErrorCode`)

| Enum Value         | Código Público           | HTTP | Descrição |
| -------------------- | --------------------------- | ---: | --------- |
| `UnexpectedError`    | `INTERNAL_ERROR`            |  500 | Qualquer exceção não tratada — nunca vaza tipo/mensagem/stack trace reais (exceto em desenvolvimento local, com `Observability:EnableDetailedErrors=true`, nunca em `appsettings.json` versionado) |
| `ServiceUnavailable` | `SERVICE_UNAVAILABLE`       |    — | Reservado |

## Rate limiting (`RateLimitErrorCode`)

| Enum Value        | Código Público   | HTTP | Descrição |
| -------------------- | ------------------ | ---: | --------- |
| `TooManyRequests`    | `RATE_LIMITED`     |  429 | Qualquer uma das políticas de rate limiting (`public-files`, `auth`, `password-reset`) rejeitou a requisição — mesmo código para todas; a política específica não é exposta ao cliente |

## Por que "reservado" para vários valores

Vários enums acima existem porque o exercício pediu a padronização completa dos sete grupos (`AuthErrorCode`, `FileErrorCode`, `AuthorizationErrorCode`, `ValidationErrorCode`, `ResourceErrorCode`, `SystemErrorCode`, `RateLimitErrorCode`) como fundação estável para o futuro, mesmo onde a condição correspondente ainda não existe no sistema (ex.: conta desabilitada) ou já é coberta por outro mecanismo (`VALIDATION_ERROR`). Os fluxos de autenticação, recuperação de senha e **arquivos** (`FilesController`/`PublicFilesController`, incluindo upload, conclusão de upload, geração de link, acesso público e download) usam todos o mesmo mecanismo — `throw` de uma exceção tipada, capturada pelo `GlobalExceptionHandler` — não existe mais um segundo mecanismo de erro concorrente (`Result<T>`/`FailureReason`) para nenhum desses fluxos.

## Testes

Cobertura garantida por `ErrorCodeCatalogTests` (todo código declarado tem mapeamento, nenhum código duplicado entre enums) e `AppExceptionTests` (cada exceção carrega o `HttpStatusCode`/`PublicCode`/enum corretos) em `FileSharing.UnitTests`, mais os testes de integração HTTP end-to-end em `FileSharing.ApiTests` (`Auth/PasswordResetEndpointsTests`, `Auth/PasswordResetEnumerationTests`, `Auth/PasswordResetRateLimitingTests`, `Files/FilesEndpointsTests`, `Files/FilesQueryEndpointsTests`, `Files/PublicFilesEndpointsTests`, `Files/PublicFileDownloadEndpointsTests`).
