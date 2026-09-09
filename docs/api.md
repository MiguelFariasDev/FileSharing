# API

Esta seção documenta os endpoints implementados até a Etapa 2 (Autenticação e JWT).
Endpoints de upload, download, links públicos e notificações serão documentados nas etapas correspondentes.

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

## Autenticação nas requisições

Endpoints protegidos exigem o cabeçalho:

```
Authorization: Bearer {accessToken}
```

O Swagger (`/swagger`, apenas em ambiente de desenvolvimento) expõe o botão **Authorize** para informar o token e testar os endpoints protegidos diretamente pela interface.
