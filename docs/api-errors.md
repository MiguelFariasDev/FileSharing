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
| `NotFound`             | `FILE_NOT_FOUND`               |    — | Reservado — os endpoints de arquivo hoje usam o `Result<T>`/`FailureReason` já existente (ver `docs/architecture.md`), não esta exceção; mantido para uso futuro/consistência com o restante do catálogo |
| `Expired`              | `FILE_EXPIRED`                  |    — | Reservado |
| `AccessDenied`         | `FILE_ACCESS_DENIED`           |    — | Reservado |
| `InvalidType`          | `FILE_INVALID_TYPE`            |    — | Reservado — hoje coberto por `VALIDATION_ERROR` |
| `InvalidUploadState`   | `FILE_UPLOAD_INVALID_STATE`    |    — | Reservado |
| `InvalidFileName`      | `FILE_INVALID_NAME`            |    — | Reservado — hoje coberto por `VALIDATION_ERROR` |
| `UploadNotCompleted`   | `FILE_UPLOAD_NOT_COMPLETED`    |    — | Reservado |

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

Vários enums acima existem porque o exercício pediu a padronização completa dos sete grupos (`AuthErrorCode`, `FileErrorCode`, `AuthorizationErrorCode`, `ValidationErrorCode`, `ResourceErrorCode`, `SystemErrorCode`, `RateLimitErrorCode`) como fundação estável para o futuro — mas esta fase focou a implementação real em autenticação/recuperação de senha (onde a mudança de comportamento é visível e testada). Os controllers de arquivos (`FilesController`) continuam usando o mecanismo `Result<T>`/`FailureReason` já existente antes desta fase (ver `docs/architecture.md`) — migrá-los para o novo mecanismo de exceções é possível a qualquer momento, reaproveitando os mesmos enums/`ErrorCodeCatalog` já documentados aqui, sem exigir nenhum enum novo.

## Testes

Cobertura garantida por `ErrorCodeCatalogTests` (todo código declarado tem mapeamento, nenhum código duplicado entre enums) e `AppExceptionTests` (cada exceção carrega o `HttpStatusCode`/`PublicCode`/enum corretos) em `FileSharing.UnitTests`, mais os testes de integração HTTP end-to-end em `FileSharing.ApiTests` (`Auth/PasswordResetEndpointsTests`, `Auth/PasswordResetEnumerationTests`, `Auth/PasswordResetRateLimitingTests`).
