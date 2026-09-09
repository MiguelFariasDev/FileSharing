# FileSharing

Plataforma de compartilhamento temporário e seguro de arquivos, inspirada no modelo do WeTransfer. Um usuário autenticado envia um arquivo a partir de um aplicativo Android (.NET MAUI), o arquivo é armazenado de forma privada no Amazon S3, e um link público de compartilhamento é gerado com validade de exatamente 24 horas. O remetente recebe uma notificação em tempo real assim que o link é acessado e o download é realizado.

## Visão geral

O projeto foi construído para demonstrar práticas de engenharia de software orientadas a produção, cobrindo:

- Backend em C#/.NET com Clean Architecture
- Aplicativo mobile nativo Android em .NET MAUI
- Frontend Web em Blazor Server para o painel do remetente e para a página pública de download
- Armazenamento de arquivos em nuvem via Amazon S3, usando URLs pré-assinadas (o arquivo nunca trafega pelo backend)
- Expiração automática de arquivos via job recorrente (Hangfire)
- Notificações em tempo real via SignalR quando um arquivo é baixado
- Infraestrutura como parte do fluxo de desenvolvimento (Docker Compose, LocalStack) e deploy em AWS (ECS Fargate, RDS, S3)

## Arquitetura

O backend segue Clean Architecture, com a seguinte direção de dependência:

```
Domain
   ↑
Application
   ↑
Infrastructure
   ↑
API
```

- **Domain**: entidades e regras de negócio puras (ex: cálculo de expiração, geração de token de compartilhamento), sem nenhuma dependência externa.
- **Application**: casos de uso (via MediatR), interfaces para infraestrutura (armazenamento, notificação, repositórios) e validações (FluentValidation).
- **Infrastructure**: implementações concretas — Entity Framework Core, cliente do S3, jobs do Hangfire, integração com o SignalR.
- **API**: camada fina de composição — controllers, injeção de dependência, middlewares e configuração do Swagger.

### Fluxo de upload e compartilhamento

```
[App Android] → [API: solicita URL pré-assinada] → [Upload direto ao S3]
                                                            ↓
                                                [API gera link com token único]
                                                            ↓
[Destinatário acessa o link] → [API valida expiração] → [URL pré-assinada de download]
                                                            ↓
                                                [API registra o download no banco]
                                                            ↓
                                        [SignalR notifica o remetente em tempo real]
```

### Expiração automática

Um job recorrente (Hangfire), executado a cada 15 minutos, verifica todos os arquivos ativos cujo prazo de 24 horas expirou, remove o objeto correspondente do S3 e atualiza o status no banco de dados. O job é idempotente: reexecuções não geram erro caso o arquivo já tenha sido removido anteriormente.

## Tecnologias utilizadas

**Backend**
- .NET 10 / ASP.NET Core Web API
- Entity Framework Core + PostgreSQL (Npgsql)
- Autenticação JWT
- SignalR (notificações em tempo real)
- Hangfire (jobs recorrentes de expiração)
- Serilog (logging estruturado)

**Mobile**
- .NET MAUI (Android)
- CommunityToolkit.Mvvm
- HttpClient com suporte a progresso de upload

**Web**
- Blazor Server
- SignalR (cliente)

**Nuvem (AWS)**
- ECS Fargate (hospedagem da API)
- RDS PostgreSQL (banco de dados)
- S3 (armazenamento dos arquivos)
- Secrets Manager (gestão de segredos)
- Application Load Balancer
- CloudWatch (observabilidade)

**Desenvolvimento**
- Docker Compose
- LocalStack (simulação do S3 em ambiente local)
- GitHub Actions (CI/CD)

## Requisitos funcionais

1. Cadastro e login de usuário com autenticação JWT
2. Upload de arquivo pelo aplicativo Android, com envio direto ao S3 via URL pré-assinada
3. Expiração automática e fixa em 24 horas a partir do momento do upload
4. Geração de link único, não sequencial e não adivinhável para compartilhamento
5. Acesso ao arquivo exclusivamente através do link, sem listagem pública
6. Verificação de validade do link no momento do acesso, com mensagem apropriada em caso de expiração
7. Registro de cada download realizado (data/hora, IP, user-agent)
8. Notificação em tempo real no painel do remetente quando o arquivo é baixado
9. Painel com status do arquivo (ativo/expirado), tempo restante e histórico de downloads

## Requisitos não funcionais

1. Token de acesso ao link com entropia suficiente para resistir a tentativas de força bruta
2. Reconexão automática do canal de notificações em tempo real (SignalR) em caso de queda de rede
3. Job de expiração executado com frequência suficiente para manter a exclusão próxima ao horário real de expiração
4. Limitação de requisições (rate limiting) na rota pública de acesso ao link
5. Cobertura de testes automatizados para os fluxos críticos, incluindo o cenário de token expirado ou inexistente
6. Nenhum segredo (strings de conexão, chaves JWT, credenciais AWS) deve ser versionado no repositório

## Estrutura do projeto

```
FileSharing.sln
src/
  FileSharing.Domain/
  FileSharing.Application/
  FileSharing.Infrastructure/
  FileSharing.Api/
  FileSharing.Web/              # Projeto Blazor Server
  FileSharing.Mobile/           # Projeto .NET MAUI (Android)
  FileSharing.Shared/           # Contratos/DTOs compartilhados entre Api, Web e Mobile
tests/
  FileSharing.Domain.Tests/
  FileSharing.Application.Tests/
  FileSharing.Api.IntegrationTests/
docker-compose.yml
.github/workflows/
```

## Como executar localmente

### Pré-requisitos
- .NET SDK 10
- Docker e Docker Compose
- Workload do MAUI instalado (`dotnet workload install maui`)

### Passos

```bash
# appsettings.Development.json é git-ignorado (nunca versionar strings de conexão/credenciais) —
# copie o template com os valores de desenvolvimento do LocalStack (não são segredos reais)
cp src/FileSharing.Api/appsettings.Development.json.example src/FileSharing.Api/appsettings.Development.json

# Subir o PostgreSQL e o LocalStack (simulação do S3)
cd infrastructure/docker
docker compose up -d
cd ../..

# Configurar o segredo do JWT (uma vez por máquina de desenvolvimento)
dotnet user-secrets set "Jwt:SecretKey" "<chave-aleatoria-de-pelo-menos-32-bytes>" --project src/FileSharing.Api

# Aplicar as migrações do banco de dados
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update -p src/FileSharing.Infrastructure -s src/FileSharing.Api

# Rodar a API (usa appsettings.Development.json: Postgres na porta 5433, LocalStack em http://localhost:4566)
dotnet run --project src/FileSharing.Api

# Rodar o frontend Blazor
dotnet run --project src/FileSharing.Web
```

Para o aplicativo Android, abra `src/FileSharing.Mobile` no Visual Studio Code com a extensão C# Dev Kit e execute em um emulador Android configurado localmente. Veja `docs/architecture.md` para os detalhes do fluxo de upload MAUI → API → S3.

## Testes

```bash
dotnet test tests/FileSharing.UnitTests
dotnet test tests/FileSharing.ApiTests

# Requer o LocalStack ativo (docker compose up -d em infrastructure/docker)
dotnet test tests/FileSharing.IntegrationTests
```

## Escopo desta versão

O escopo atual do projeto contempla apenas o cliente mobile Android. Uma versão para iOS está planejada como evolução futura, utilizando .NET MAUI com pipeline de build via GitHub Actions em runner macOS, dado que a compilação e assinatura de aplicativos iOS exigem obrigatoriamente ferramentas da Apple (Xcode), indisponíveis em ambiente Linux.

## Autor

Miguel — projeto desenvolvido como parte de portfólio técnico, com foco em backend distribuído, integração com serviços de nuvem AWS e comunicação em tempo real.