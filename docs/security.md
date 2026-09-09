# Segurança

Documentação da segurança implementada até a Etapa 2 (Autenticação e JWT). Tópicos de S3, links públicos e expiração de arquivos serão documentados nas etapas correspondentes.

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

## Outras práticas aplicadas

- Todas as datas são tratadas em UTC (`DateTimeOffset.UtcNow`), nunca `DateTime.Now`.
- `RegisterAsync` trata `DbUpdateException` (violação da constraint única de `Email`) como cadastro duplicado, cobrindo condições de corrida entre a checagem de existência e a inserção.
- `IApplicationDbContext` mantém a Application layer livre de referência direta a `Npgsql`/EF Core de infraestrutura; a implementação concreta (`ApplicationDbContext`) fica em `FileSharing.Infrastructure`.
