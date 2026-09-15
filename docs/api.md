# API

Esta seção documenta os endpoints implementados até a Etapa 8 (Autenticação/JWT + Upload de arquivos + Link público de acesso + Download + histórico de downloads + Hangfire/expiração automática + SignalR/notificação em tempo real + Blazor Web/Dashboard), além da fase de recuperação de senha e padronização de erros descrita abaixo.

## Formato padronizado de erro

Toda resposta de erro desta API (validação, autenticação, autorização, conflito, recurso não encontrado, rate limit, ou erro interno) segue o mesmo formato, um `ProblemDetails` (RFC 9110) com duas extensões próprias:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2",
  "title": "E-mail ou senha inválidos.",
  "status": 401,
  "traceId": "00-6334ae22bf58b33595fd6285d334a56b-24f3b0cdc9743f36-00",
  "correlationId": "708dd15a9fb547399947bca554eac0d2",
  "code": "AUTH_INVALID_CREDENTIALS"
}
```

- **`code`**: string estável e pública — o único campo que Mobile/Web devem usar para tomar decisões de fluxo (ex.: distinguir um token de recuperação expirado de um já utilizado, que retornam o mesmo `status` 410). Ver o catálogo completo em `docs/api-errors.md`. Nunca é o nome do enum interno (`AuthErrorCode.InvalidCredentials` etc.) — é sempre derivado por um mapeamento explícito (`ErrorCodeCatalog`), o que permite renomear o enum no futuro sem quebrar clientes.
- **`title`**: mensagem em português, segura para exibir ao usuário — nunca uma mensagem de exceção, stack trace, ou detalhe de infraestrutura (SQL, AWS, etc.).
- **`correlationId`**: id desta requisição, o mesmo já usado desde a Etapa 12 (`X-Correlation-ID` no header de resposta e em todas as linhas de log desta requisição).
- **`traceId`**: id de distributed tracing padrão do ASP.NET Core (formato W3C), gerado automaticamente pelo framework — coexiste com `correlationId` (que é o identificador que este projeto usa em seus próprios logs/documentação) sem ser a mesma string.
- **`errors`**: presente **somente** em erros de validação (`code: "VALIDATION_ERROR"`) — dicionário campo → lista de mensagens, no formato padrão do `ValidationProblemDetails` do ASP.NET Core.

Erros inesperados (uma exceção não tratada — EF Core, AWS SDK, um bug) nunca vazam o tipo/mensagem/stack trace real: são sempre convertidos para `code: "INTERNAL_ERROR"` com uma mensagem genérica, em qualquer ambiente. Ver `docs/security.md`.

A Etapa 6 (Hangfire + limpeza automática de arquivos expirados) **não adiciona nenhum endpoint HTTP novo** — é um job recorrente em segundo plano, sem superfície de API própria, e deliberadamente sem Dashboard exposto (`/hangfire` não existe como rota nesta etapa; ver `docs/security.md`). Ver `docs/architecture.md` para o funcionamento do job e a relação entre `ExpiresAt` e a limpeza.

A Etapa 7 (SignalR + notificação em tempo real) adicionou um único endpoint novo, não-REST: o Hub abaixo. `GET /api/public/files/{token}/download` (Etapa 5) continua exatamente o mesmo — nenhum campo novo na requisição/resposta, nenhuma mudança de comportamento visível para o downloader.

A Etapa 8 (Blazor Web/Dashboard) adicionou dois endpoints mínimos que faltavam para o dashboard funcionar — `GET /api/files/mine` e `GET /api/files/{id}/downloads`, ambos abaixo — e nenhum outro: nenhum endpoint existente foi alterado, e nenhum endpoint foi duplicado.

## Hub /hubs/notifications

Endpoint SignalR, **não** um controller REST. Exige um JWT autenticado (mesmo token emitido por `POST /api/auth/login`) — uma tentativa de conexão sem token recebe `401` na negociação, antes mesmo de qualquer conexão ser estabelecida.

- **Identidade:** `Context.UserIdentifier` é derivado do claim `sub` do JWT (nunca de algo enviado pelo cliente) — ver `docs/architecture.md`/`docs/security.md` para o porquê de um `IUserIdProvider` customizado ser necessário aqui.
- **Transporte do token:** para clientes REST normais, o JWT continua indo no header `Authorization: Bearer <token>`. Para a conexão SignalR especificamente (necessário para WebSockets/SSE em um navegador, que não conseguem anexar esse header a um upgrade de conexão), o token também pode ser enviado como `?access_token=<token>` — **só** nesse caminho (`/hubs/notifications`); em qualquer outro endpoint esse parâmetro é ignorado e o header continua obrigatório.
- **Evento enviado pelo servidor:** `"FileDownloaded"`, disparado quando `GET /api/public/files/{token}/download` autoriza e registra um download de um arquivo que pertence ao usuário conectado.

  ```json
  {
    "fileId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "originalFileName": "document.pdf",
    "downloadedAt": "2026-09-12T12:34:56.0000000+00:00"
  }
  ```

  Nenhum outro campo é enviado — em particular, nunca o token de acesso público, o `AccessTokenHash`, a presigned URL, o `StorageKey`, ou o IP/User-Agent de quem baixou (esses últimos ficam só no histórico `GET /api/files/{id}/downloads`, quando esse endpoint existir).

- **Sem métodos invocáveis pelo cliente.** O Hub só empurra eventos para o servidor conectado — não expõe nenhuma ação que o cliente possa chamar.
- **"FileDownloaded" representa "um download foi autorizado/registrado", não "o downloader terminou de baixar o arquivo"** — a API não tem como observar o fim de uma transferência que acontece direto entre o downloader e o S3 (mesma ressalva já documentada para o registro `Download` da Etapa 5).

Todas as respostas de erro de validação seguem o formato padrão do ASP.NET Core (`ValidationProblemDetails`, 400).

---

## POST /api/auth/register

Cria um novo usuário.

Autenticação: não requerida.

### Request

```json
{
  "email": "user@example.com",
  "password": "SenhaForte123"
}
```

- `email`: obrigatório, formato de e-mail válido, máximo 320 caracteres. É normalizado (trim + lowercase) antes de ser persistido.
- `password`: obrigatório, entre 8 e 100 caracteres. Nunca é armazenado — apenas seu hash.

### Responses

- `201 Created`
  ```json
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "user@example.com"
  }
  ```
- `400 Bad Request`: request inválido (`ValidationProblemDetails`, `code: "VALIDATION_ERROR"`).
- `409 Conflict`: e-mail já cadastrado.
  ```json
  { "title": "Não foi possível concluir o cadastro.", "status": 409, "code": "AUTH_EMAIL_ALREADY_EXISTS", "correlationId": "...", "traceId": "..." }
  ```

---

## POST /api/auth/login

Autentica um usuário existente e retorna um token JWT.

Autenticação: não requerida.

### Request

```json
{
  "email": "user@example.com",
  "password": "SenhaForte123"
}
```

### Responses

- `200 OK`
  ```json
  {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "expiresAt": "2026-09-09T15:30:00+00:00"
  }
  ```
- `400 Bad Request`: request inválido.
- `401 Unauthorized`: credenciais inválidas (e-mail inexistente ou senha incorreta — a mensagem é sempre a mesma, para não revelar qual delas falhou).
  ```json
  { "title": "Credenciais inválidas.", "status": 401, "code": "AUTH_INVALID_CREDENTIALS", "correlationId": "...", "traceId": "..." }
  ```

---

## GET /api/auth/me

Retorna os dados do usuário autenticado.

Autenticação: **obrigatória** (`Authorization: Bearer {accessToken}`).

O `id` do usuário é obtido exclusivamente a partir do claim `sub` do token — nunca de um parâmetro enviado pelo cliente.

### Response

- `200 OK`
  ```json
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "user@example.com"
  }
  ```
- `401 Unauthorized`: token ausente, inválido ou expirado.

---

## POST /api/files/upload

Inicia um upload. **Não recebe o conteúdo do arquivo** — apenas metadata. Retorna uma presigned URL para o cliente enviar os bytes diretamente ao S3.

Autenticação: **obrigatória**.

### Request

```json
{
  "fileName": "documento.pdf",
  "contentType": "application/pdf",
  "sizeBytes": 1048576,
  "isFolder": false
}
```

Para uma pasta (já compactada em `.zip` pelo cliente antes de chamar este endpoint):

```json
{
  "fileName": "MinhaPasta.zip",
  "contentType": "application/zip",
  "sizeBytes": 5000000,
  "isFolder": true
}
```

`compressionType` não é um campo do request: para esta versão ele é sempre determinado pelo servidor a partir de `isFolder` (`Zip` quando `true`, `None` quando `false`) — não é confiável nem necessário receber essa decisão do cliente.

- `fileName`: obrigatório, máximo 255 caracteres, a extensão deve ser consistente com `contentType`.
- `contentType`: obrigatório, deve estar na allowlist de `FileTypePolicy` (PDF, EPUB, JPEG/PNG/WebP/GIF, MP4/WebM/MOV/MKV, MP3/WAV/OGG/M4A/AAC/FLAC, ZIP).
- `sizeBytes`: obrigatório, maior que zero e menor ou igual a `FileStorage:MaxFileSizeBytes` (5 GB por padrão, configurável).
- `isFolder`: quando `true`, `contentType` deve ser `application/zip`.

### Responses

- `201 Created`
  ```json
  {
    "fileId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "uploadUrl": "https://<bucket>.s3.<region>.amazonaws.com/<storageKey>?X-Amz-...",
    "expiresAt": "2026-09-09T10:15:00+00:00"
  }
  ```
  `expiresAt` aqui é a expiração da **presigned URL** (minutos, curta) — não confundir com a expiração de 24h do arquivo, que só é definida no `complete`.
- `400 Bad Request`: request inválido (tipo não permitido, extensão inconsistente, tamanho fora do limite etc.) — `ValidationProblemDetails`.
- `401 Unauthorized`: sem JWT válido.

O `File` criado começa com `Status = PendingUpload` — ainda não é considerado um arquivo disponível.

---

## POST /api/files/{id}/complete

Confirma que o upload para o S3 terminou. Chamado pelo cliente **depois** que o `PUT` na presigned URL retornar sucesso.

Autenticação: **obrigatória**. Só o dono do `File` pode completá-lo.

### Fluxo de validação (a API nunca confia no cliente aqui)

1. O `File` existe e pertence ao usuário autenticado — senão, `404` (mesma resposta para "não existe" e "pertence a outro usuário": nunca revela a diferença).
2. O `File` está `PendingUpload` — senão, `409 Conflict` (já completado, ou nunca chegou a existir como upload pendente).
3. O objeto existe no S3 (`HEAD`/`GetObjectMetadata`) — senão, `409 Conflict`.
4. O tamanho real do objeto bate com o `SizeBytes` declarado no `initiate` — senão, `409 Conflict`.
5. O Content-Type do objeto (quando o S3 o retorna) bate com o declarado — senão, `409 Conflict`.

Só depois de todas essas checagens passarem o `File` vira `Active`.

### Response

- `200 OK`
  ```json
  {
    "fileId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "originalFileName": "documento.pdf",
    "sizeBytes": 1048576,
    "createdAt": "2026-09-09T10:18:32+00:00",
    "expiresAt": "2026-09-10T10:18:32+00:00"
  }
  ```
  `createdAt` é o instante desta confirmação (não o instante em que a presigned URL foi emitida); `expiresAt` é sempre `createdAt + 24 horas`.
- `401 Unauthorized`: sem JWT válido.
- `404 Not Found`: arquivo inexistente ou de outro usuário.
- `409 Conflict`: arquivo não está `PendingUpload`, objeto ausente no S3, ou metadata inconsistente.

## POST /api/files/{id}/link

Gera (ou regera) o link público de acesso a um `File`. Chamado pelo dono do arquivo, tipicamente depois que `POST /api/files/{id}/complete` retornou sucesso.

Autenticação: **obrigatória**. Só o dono do `File` pode gerar o link.

### Fluxo de validação

1. O `File` existe e pertence ao usuário autenticado — senão, `404` (mesma resposta para "não existe" e "pertence a outro usuário": nunca revela a diferença, mesmo padrão do `complete`).
2. O `File` está `Active` — senão (ainda `PendingUpload`, ou qualquer outro estado que não seja `Active`), `409 Conflict`. Não é possível gerar link para um upload ainda não confirmado.
3. `ExpiresAt` ainda não passou (`DateTimeOffset.UtcNow < ExpiresAt`) — senão, `409 Conflict`. Um arquivo expirado não recebe novo link, mesmo que seu `Status` ainda esteja `Active` (a transição para `Status = Expired` só acontece no job de limpeza, de uma etapa futura; a validação de expiração aqui **não depende** desse job).

Cada chamada bem-sucedida gera um **token novo**, aleatório e não relacionado ao anterior, e substitui o hash anteriormente associado ao `File` — um link antigo emitido por uma chamada anterior deixa de funcionar assim que um novo é gerado (só o hash é persistido; o token em texto puro de uma chamada anterior não pode ser recuperado nem invalidado seletivamente).

### Response

- `200 OK`
  ```json
  {
    "fileId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "accessToken": "Xk8pQ2m...v9Z",
    "publicUrl": "https://api.example.com/api/public/files/Xk8pQ2m...v9Z"
  }
  ```
  `accessToken` é retornado **somente nesta resposta** — o servidor nunca o persiste em texto puro (ver `docs/security.md`) e não há como consultá-lo novamente depois; se perdido, é necessário gerar um novo link (o que invalida o anterior). `publicUrl` é montado a partir do `scheme`/`host` da própria requisição recebida (nunca um domínio fixo no código), então reflete automaticamente o ambiente em que a API está rodando (local, staging, produção atrás do ALB).
- `401 Unauthorized`: sem JWT válido.
- `404 Not Found`: arquivo inexistente ou de outro usuário.
- `409 Conflict`: arquivo ainda não concluído (`PendingUpload`) ou já expirado.

---

## GET /api/files/mine

Adicionado na Etapa 8 para o dashboard Web — lista os arquivos do usuário autenticado. Nenhum endpoint equivalente existia antes; nenhum endpoint duplicado foi criado (a listagem sempre foi feita por este único endpoint desde que passou a existir).

Autenticação: **obrigatória**. Retorna exclusivamente arquivos cujo `UserId` é o do usuário do JWT — determinado sempre pelo claim `sub`, nunca por um parâmetro que o cliente possa manipular. Um usuário jamais recebe arquivos de outro (ver `docs/security.md`).

### Response

- `200 OK`
  ```json
  [
    {
      "fileId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "originalFileName": "relatorio.pdf",
      "contentType": "application/pdf",
      "sizeBytes": 204800,
      "isFolder": false,
      "status": "Active",
      "createdAt": "2026-09-12T10:00:00+00:00",
      "expiresAt": "2026-09-13T10:00:00+00:00",
      "downloadCount": 2,
      "hasPublicLink": true
    }
  ]
  ```
  `status` é a representação em string de `FileStatus` (`"PendingUpload"`, `"Active"` ou `"Expired"`). `createdAt`/`expiresAt` são `null` enquanto `PendingUpload` (mesma nulabilidade documentada em `docs/database.md`). `hasPublicLink` reflete apenas se `AccessTokenHash` já foi definido (`!= null`) — **nunca** o hash em si, e nunca o token em texto puro (que nem sequer é persistido). Um novo `accessToken` só é obtido chamando `POST /api/files/{id}/link` novamente.
- `401 Unauthorized`: sem JWT válido.

---

## GET /api/files/{id}/downloads

Adicionado na Etapa 8 para o histórico de downloads do dashboard. Owner-only, no mesmo padrão de `complete`/`link`.

Autenticação: **obrigatória**.

### Response

- `200 OK`
  ```json
  [
    { "downloadedAt": "2026-09-12T15:32:10+00:00" },
    { "downloadedAt": "2026-09-12T11:04:02+00:00" }
  ]
  ```
  Ordenado do mais recente para o mais antigo. Deliberadamente **não inclui** `ipAddress`/`userAgent` — esses campos existem no banco (`downloads`, Etapa 5) para fins de auditoria, mas não são expostos neste endpoint nesta etapa (ver `docs/security.md`).
- `401 Unauthorized`: sem JWT válido.
- `404 Not Found`: arquivo inexistente ou pertence a outro usuário — mesma resposta genérica para ambos os casos.

---

## GET /api/public/files/{token}

Endpoint **público** (sem autenticação) que resolve um link compartilhado. Recebe o token em texto puro, calcula seu hash e localiza o `File` por esse hash — nunca por `File.Id`.

Autenticação: **não requerida**.

Rate limiting: aplicado (política `public-files`, janela fixa de 1 minuto) para dificultar tentativas de força bruta contra o espaço de tokens — ver `docs/security.md`.

### Comportamento

- Token válido, arquivo `Active` e `ExpiresAt` ainda não atingido: `200 OK` com as informações mínimas necessárias para um futuro fluxo de download.
- Qualquer outro caso — token nunca emitido, arquivo expirado (por tempo, mesmo que `Status` ainda esteja `Active` na ausência do job de limpeza), ou arquivo em qualquer estado diferente de `Active` — retorna **exatamente a mesma resposta**: `404 Not Found`, sem detalhe adicional. Um atacante não consegue diferenciar "token nunca existiu" de "arquivo expirou" a partir da resposta.
- O token recebido nunca é ecoado de volta na resposta (de sucesso ou de erro) nem aparece em log algum.

### Response

- `200 OK`
  ```json
  {
    "fileId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "originalFileName": "documento.pdf",
    "sizeBytes": 1048576,
    "contentType": "application/pdf",
    "expiresAt": "2026-09-10T10:18:32+00:00"
  }
  ```
  Esta resposta **não** inclui `storageKey`, `userId` nem uma URL de download — a emissão de uma presigned GET URL e o registro do `Download` acontecem em `GET /api/public/files/{token}/download`, abaixo.
- `404 Not Found`: token inexistente, expirado, ou associado a um arquivo que não está `Active` — resposta genérica, idêntica em todos os casos.
- `429 Too Many Requests`: limite de requisições excedido (rate limiting).

---

## GET /api/public/files/{token}/download

Endpoint **público** (sem autenticação) que autoriza e registra um download, e devolve uma presigned URL de curta duração para o cliente baixar o objeto **diretamente do S3** — o conteúdo do arquivo nunca passa por esta API, no mesmo espírito do upload (Etapa 3).

Autenticação: **não requerida**.

Rate limiting: **mesma política** `public-files` do `GET /api/public/files/{token}` (ver `docs/security.md`) — os dois endpoints compartilham o mesmo orçamento de requisições, já que ambos expõem a mesma superfície de ataque (adivinhação de token).

### Fluxo de validação

1. Calcula o hash do token recebido e localiza o `File` por `AccessTokenHash` — nunca por `File.Id` (não existe, e não é criado nesta etapa, nenhum endpoint de download baseado só em `/api/files/{id}/download`).
2. `File.Status == Active` — senão, `404`.
3. `ExpiresAt` ainda não atingido (`DateTimeOffset.UtcNow < ExpiresAt`) — verificado **imediatamente**, sem depender de nenhum job de limpeza (Hangfire só chega na Etapa 6); senão, `404`.
4. O objeto correspondente (`File.StorageKey`) existe no S3/LocalStack (`IFileStorageService.ObjectExistsAsync`) — senão, `404`. A resposta nunca diz "objeto não encontrado no S3": é o mesmo `404` genérico de qualquer outra causa.
5. Só depois de 1–4 passarem: registra um `Download` (`FileId`, `DownloadedAt` UTC, `IpAddress`, `UserAgent`) e persiste.
6. Gera uma presigned **GET** URL específica para download (`IFileStorageService.CreatePresignedDownloadUrlAsync`) — nunca a mesma presigned URL do upload, e com expiração curta e independente (`FileStorage:DownloadUrlExpirationSeconds`, 300s por padrão), nunca os mesmos minutos do upload nem as 24h de `File.ExpiresAt`.

Qualquer uma das etapas 1–4 falhando retorna o **mesmo** `404` genérico — token nunca emitido, arquivo expirado, arquivo em qualquer status diferente de `Active`, e objeto ausente no S3 são todos indistinguíveis na resposta. Nenhum `Download` é registrado para uma tentativa que não passou em todas as checagens.

### O que "download" significa aqui

Esta API não tem como saber se o cliente realmente terminou de baixar os bytes depois de receber a presigned URL — só sabe que emitiu uma. Por isso, o evento registrado em `Download` é **"download autorizado/iniciado"** (uma presigned URL válida foi emitida a quem apresentou um token válido para um arquivo disponível), não "download concluído". Ver `docs/architecture.md`.

O link público pode ser usado **múltiplas vezes** enquanto o arquivo permanecer `Active` e não expirado — cada chamada gera seu próprio registro de `Download` e sua própria presigned URL; nenhuma chamada altera `File.Status` ou consome o token.

### Response

- `200 OK`
  ```json
  {
    "downloadUrl": "https://filesharing-dev.s3.us-east-1.amazonaws.com/<storageKey>?X-Amz-...",
    "expiresAt": "2026-09-09T10:23:32+00:00"
  }
  ```
  `expiresAt` aqui é a expiração da **presigned URL de download** (minutos, curta — `FileStorage:DownloadUrlExpirationSeconds`), não a expiração de 24h do arquivo. A resposta não contém `accessToken`, `AccessTokenHash`, `storageKey` nem qualquer outro dado além da URL e sua expiração.
- `404 Not Found`: token inexistente, expirado, arquivo em status diferente de `Active`, ou objeto ausente no S3 — resposta genérica, idêntica em todos os casos.
- `429 Too Many Requests`: limite de requisições excedido (rate limiting).

## Autenticação nas requisições

Endpoints protegidos exigem o cabeçalho:

```
Authorization: Bearer {accessToken}
```

O Swagger (`/swagger`, apenas em ambiente de desenvolvimento) expõe o botão **Authorize** para informar o token e testar os endpoints protegidos diretamente pela interface.
