# Segurança

Documentação da segurança implementada até a Etapa 3 (Autenticação/JWT + Upload de arquivos). Link público de download, rate limiting da rota pública e expiração automática (job em background) serão documentados nas etapas correspondentes.

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

### Por que a API falha sem configuração

`Jwt:SecretKey` vazio e `ConnectionStrings:Postgres` ausente já faziam a API recusar-se a subir corretamente (Etapa 2). O mesmo princípio se aplica ao storage: sem `FileStorage:BucketName`/`AWS:ServiceURL` configurados, as chamadas ao S3 simplesmente falham (o cliente tenta o AWS real, sem credenciais/bucket válidos) em vez de silenciosamente fingir sucesso — nunca há um "modo mock" implícito em produção.

## Outras práticas aplicadas

- Todas as datas são tratadas em UTC (`DateTimeOffset.UtcNow`), nunca `DateTime.Now`.
- `RegisterAsync` trata `DbUpdateException` (violação da constraint única de `Email`) como cadastro duplicado, cobrindo condições de corrida entre a checagem de existência e a inserção.
- `IApplicationDbContext` mantém a Application layer livre de referência direta a `Npgsql`/EF Core de infraestrutura; a implementação concreta (`ApplicationDbContext`) fica em `FileSharing.Infrastructure`.
