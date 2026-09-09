# API

Esta seção documenta os endpoints implementados até a Etapa 3 (Autenticação/JWT + Upload de arquivos).
Endpoints de download público, links compartilháveis e notificações serão documentados nas etapas correspondentes.

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
- `400 Bad Request`: request inválido (`ValidationProblemDetails`).
- `409 Conflict`: e-mail já cadastrado.
  ```json
  { "message": "Não foi possível concluir o cadastro." }
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
  { "message": "Credenciais inválidas." }
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

## Autenticação nas requisições

Endpoints protegidos exigem o cabeçalho:

```
Authorization: Bearer {accessToken}
```

O Swagger (`/swagger`, apenas em ambiente de desenvolvimento) expõe o botão **Authorize** para informar o token e testar os endpoints protegidos diretamente pela interface.
