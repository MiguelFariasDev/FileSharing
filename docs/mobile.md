# Mobile (Fase 13)

`FileSharing.Mobile` (.NET MAUI, Android — `net10.0-android`) é o cliente que efetivamente faz upload de arquivos: cria a conta, autentica, seleciona um arquivo/pasta, envia direto para o S3 via presigned URL, confirma o upload, mostra o link público, recebe notificação em tempo real quando alguém baixa, e mostra o histórico. Este documento cobre a Fase 13 — a app anterior a esta fase era um protótipo de uma única tela (login + upload) sem navegação, autenticação persistente ou UI além do essencial.

---

## Arquitetura: por que existe `FileSharing.Mobile.Core`

```
FileSharing.Mobile.Core/          net10.0 (nunca net10.0-android)
  Models/                         UploadableItem, ApiResult, PublicLinkResponse, UploadStage...
  Services/
    ApiClient/                    FileSharingApiClient, ApiClientOptions
    Authentication/               AuthSession
    Upload/                       FileUploadService, S3UploadHttpClient, interfaces
    SignalR/                      SignalRNotificationService, interfaces
    Platform/                     interfaces só (INavigationService, IClipboardService, ...)
    Storage/                      interface só (ISecureStorageService)
  ViewModels/                     Login, Register, Home, Upload, FileDetails, History, FileItem

FileSharing.Mobile/               net10.0-android (+ ios/maccatalyst/windows fora do Linux)
  Services/Storage/SecureStorageService.cs      — único lugar que toca Microsoft.Maui.Storage.SecureStorage
  Services/Upload/FilePickerService.cs          — único lugar que toca Microsoft.Maui.Storage.FilePicker
  Platforms/Android/FolderPickerService.cs      — único lugar que toca Android.* diretamente
  Services/Platform/*.cs                        — implementações concretas (Clipboard, Share, MainThread, Navigation)
  Views/, Components/, Converters/, Resources/  — XAML, estilos, glass/blue theme
  MauiProgram.cs, App.xaml.cs, AppShell.xaml.cs — composição/DI/navegação
```

**Motivo prático, não estético:** neste ambiente de desenvolvimento (Linux), `FileSharing.Mobile` só compila para `net10.0-android` — um projeto de teste `net10.0` puro **não pode referenciar** um projeto `net10.0-android` (a direção de compatibilidade de TFM não permite), e uma assembly de teste `net10.0-android` **não executa** via `dotnet test` sem um emulador/dispositivo Android anexado, que este ambiente não possui. Extrair tudo o que não depende de uma API específica do Android/MAUI (ViewModels, cliente HTTP, sessão de autenticação, orquestração de upload, cliente SignalR — nenhum deles toca `Microsoft.Maui.*`/`Android.*` diretamente, só interfaces) para um projeto `net10.0` comum é o que torna `tests/FileSharing.Mobile.Tests` (também `net10.0` puro) capaz de rodar de verdade via `dotnet test`, sem simulador. Isso não é "abstração pela abstração" — é a única forma de ter testes automatizados executáveis dado o alvo Android-only deste ambiente.

O que **fica** exclusivamente no projeto `FileSharing.Mobile` (a "head"): qualquer coisa que só existe em MAUI/Android — `SecureStorage`, `FilePicker`, Storage Access Framework (`Platforms/Android/FolderPickerService.cs`), `Clipboard`/`Share`/`MainThread`, XAML (Views/Components/estilos) e a composição (`MauiProgram.cs`).

---

## Fluxo funcional completo

```
Login/Cadastro
      ↓
POST /api/auth/login  →  AuthResponse { accessToken, expiresAt }
      ↓
GET /api/auth/me      →  UserResponse
      ↓
AuthSession.SetSessionAsync(...)   — persiste em SecureStorage
      ↓
Home (GET /api/files/mine)
      ↓
Selecionar arquivo/pasta (FilePicker / Storage Access Framework + zip local)
      ↓
Preview (nome, tamanho, tipo) → Confirmar
      ↓
POST /api/files/upload  (initiate)  →  InitiateUploadResponse { fileId, uploadUrl, expiresAt }
      ↓
PUT direto para a uploadUrl (S3/LocalStack) — progresso real, byte a byte
      ↓
POST /api/files/{id}/complete  →  CompleteUploadResponse
      ↓
Arquivo Active (ExpiresAt = CreatedAt + 24h, decidido pelo backend — nunca pelo Mobile)
      ↓
POST /api/files/{id}/link  →  PublicLinkResponse { accessToken, publicUrl }
      ↓
Copiar / Compartilhar (Android Share sheet nativo)
      ↓
Destinatário acessa o link e baixa (GET /api/public/files/{token}/download — fora do Mobile)
      ↓
SignalR: FileDownloaded  →  Home atualiza contagem + mostra banner
```

**A API nunca recebe o conteúdo do arquivo.** `FileUploadService` (`FileSharing.Mobile.Core/Services/Upload/`) usa dois `HttpClient` distintos e nunca os confunde: `FileSharingApiClient`'s HttpClient (aponta para a API, sempre carrega o JWT) e `S3UploadHttpClient` (aponta para a `uploadUrl` recebida, nunca carrega o JWT nem qualquer header de autenticação da API — só a assinatura que já vem embutida na própria presigned URL). Um teste (`FileUploadServiceTests.UploadAsync_PutsDirectlyToTheProvidedPresignedUrl_NeverToTheApi`) prova isso na prática, não só por inspeção do código.

---

## Autenticação e sessão

- `ISecureStorageService`/`SecureStorageService` — única fachada sobre `Microsoft.Maui.Storage.SecureStorage` (Android Keystore) usada em todo o app.
- `AuthSession` (`FileSharing.Mobile.Core`) guarda `AccessToken`, `ExpiresAt` e o `UserResponse` atual, persiste os três via `ISecureStorageService`, e expõe `IsAuthenticated` (token presente **e** não expirado, verificado localmente contra `ExpiresAt` — nunca decodifica o JWT, não precisa). `RestoreAsync()` é chamado uma vez, na inicialização (`AppShell.xaml.cs`), para restaurar a sessão entre reinícios do app.
- `FileSharingApiClient` lê o token de `AuthSession` a cada chamada autenticada — nunca guarda um campo de token próprio (diferente da versão anterior à Fase 13, que guardava `_accessToken` internamente e se perdia ao reiniciar o app).
- `SessionExpired` (evento do `FileSharingApiClient`, disparado em qualquer 401) — nesta fase, cada ViewModel trata isso através do próprio resultado (`ApiResult.ErrorType == Unauthorized`); não há ainda um redirecionamento automático e centralizado para a tela de login a partir de um 401 vindo de qualquer lugar do app (diferença deliberada do padrão já usado no Web `SessionGuard.razor` — ver "Limitações" abaixo).
- Logout (`HomeViewModel.LogoutCommand`): para a conexão SignalR, limpa a sessão (`AuthSession.ClearSession()` remove tudo do SecureStorage), limpa a lista de arquivos em memória, navega para `//login` (raiz — nunca deixa o usuário voltar para a Home anterior).

---

## Recuperação de senha

`LoginPage` tem um botão "Esqueci minha senha" → `ForgotPasswordPage` (`ForgotPasswordViewModel`): pede o email, chama `POST /api/auth/forgot-password` e mostra sempre a mesma mensagem de sucesso, exista ou não a conta (o app nunca tem como saber a diferença — a Api responde igual nos dois casos, ver `docs/security.md`).

**Como o token chega ao app:** o link do email aponta para a página **Web** (`{PasswordReset:WebResetUrlBase}?token=...`, configurado no lado da Api) — este app não tem um deep link/App Link registrado para abrir automaticamente a partir desse link (fora do escopo desta fase: exigiria configuração de Android App Links/`assetlinks.json`, verificação de domínio, etc.). Por isso `ResetPasswordPage`/`ResetPasswordViewModel` aceita o token de **duas formas**:

1. Automaticamente, via parâmetro de rota (`resetpassword?token=...`, usando o mesmo mecanismo `IQueryAttributable` que `HistoryPage` já usa para `fileId`) — pronto para quando/se um deep link for adicionado no futuro.
2. Colado manualmente: `ForgotPasswordPage` tem um botão "Já tenho um token" → `ResetPasswordPage` sem token na rota → `ResetPasswordViewModel.NeedsManualToken = true` → um campo de texto + botão "Validar" chama `ValidateResetTokenAsync` com o valor colado.

Em ambos os casos, o token só é aceito depois de validado (`GET /api/auth/reset-password/{token}`) — o formulário de "Nova senha"/"Confirmar nova senha" só aparece após essa validação ter sucesso. Erros são mostrados por `code` (`AUTH_PASSWORD_RESET_EXPIRED`/`_USED`/`_INVALID`), nunca pela mensagem crua da Api — ver `docs/api-errors.md`.

Validado manualmente num Samsung Galaxy Note 20 físico (Android 13), ponta a ponta contra a Api real na mesma rede local: solicitação → token capturado do log do `DevelopmentEmailService` → colado na tela → validado → senha redefinida → confirmado por `POST /api/auth/login` que a senha antiga passou a falhar e a nova funciona.

---

## SecureStorage — o que entra e o que nunca entra

Só três valores, todos via `ISecureStorageService`: o JWT (`auth.access_token`), seu `ExpiresAt` (`auth.expires_at`) e o `Id`/`Email` do usuário (`auth.user_id`/`auth.email`, conveniência de exibição, não uma credencial). **Nunca**: AWS Access Key/Secret/Session Token, a chave de assinatura do JWT, credenciais do PostgreSQL, senha em texto puro — nenhum desses é sequer recebido pelo Mobile em algum momento (ver seção de Segurança abaixo).

---

## SignalR

`SignalRNotificationService` (`FileSharing.Mobile.Core/Services/SignalR/`) espelha de perto a implementação já validada em `FileSharing.Web` (Fase 7/8): `HubConnectionBuilder().WithUrl(hubUrl, options => options.AccessTokenProvider = () => Task.FromResult(_authSession.AccessToken))` + `.WithAutomaticReconnect([0s, 2s, 10s, 30s])`. O token é lido de `AuthSession` a cada (re)conexão, nunca capturado uma única vez na construção.

- `HomeViewModel` assina `FileDownloaded` uma única vez (é singleton — um dashboard, uma assinatura, para a vida inteira do app) e nunca precisa desfazer a assinatura.
- `StartAsync()` é chamado após login bem-sucedido e na restauração de sessão (`AppShell`); `StopAsync()` é chamado no logout — a conexão nunca fica aberta depois que a sessão termina.
- Payload: exatamente `FileDownloadedNotification { FileId, OriginalFileName, DownloadedAt }` — o mesmo DTO que a API e o Web já usam; nunca token, hash, presigned URL ou IP.
- Uma reconexão real de rede (queda e volta do Wi-Fi/dados) não pôde ser simulada neste ambiente (sem dispositivo — ver "Validação em dispositivo real"); o que os testes automatizados (`SignalRNotificationServiceTests`) cobrem é o que é verificável sem uma rede real: falha ao conectar não lança exceção e deixa o estado `Disconnected`, `StartAsync` chamado duas vezes concorrentemente não quebra, `DisposeAsync` nunca lança mesmo sem nunca ter iniciado.

---

## Presigned URL e S3 — o Mobile nunca vê uma credencial AWS

```
Mobile                    Api                         S3 / LocalStack
  │  POST /api/files/upload │                              │
  │ ───────────────────────>│                              │
  │                         │ gera StorageKey aleatório    │
  │                         │ assina localmente (HMAC)     │
  │  <uploadUrl>            │                              │
  │ <───────────────────────│                              │
  │                                                         │
  │  PUT <uploadUrl>  (bytes do arquivo, direto)            │
  │ ───────────────────────────────────────────────────────>│
  │  <200 OK>                                                │
  │ <─────────────────────────────────────────────────────  │
  │                                                         │
  │  POST /api/files/{id}/complete                          │
  │ ───────────────────────>│                               │
  │                         │ confere tamanho/Content-Type  │
  │                         │ contra o objeto real no S3    │
  │  <Active>               │                                │
  │ <───────────────────────│                                │
```

A `uploadUrl` já contém sua própria assinatura (`X-Amz-Signature`, `X-Amz-Credential`, `X-Amz-Date`, etc.) — o Mobile só a usa, nunca gera uma assinatura por conta própria e nunca recebe `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY`/`AWS_SESSION_TOKEN` em nenhum momento. `StorageKey` é sempre gerado pelo backend (`RandomTokenGenerator`, Fase 3) — o Mobile nunca controla o bucket, o path ou a key.

---

## Configuração (Development/Production)

`src/FileSharing.Mobile/Resources/Raw/appsettings.json` — único arquivo com a URL da API e do Hub:

```json
{
  "Api": {
    "BaseUrl": "http://10.0.2.2:5105/",
    "HubUrl": "http://10.0.2.2:5105/hubs/notifications"
  }
}
```

`10.0.2.2` é o alias que o **emulador** Android usa para alcançar o `localhost` da máquina de desenvolvimento. Em um **dispositivo físico** na mesma rede, troque pelo IP LAN da máquina rodando a API (ex.: `http://192.168.1.50:5105/`); em produção, pela URL real já implantada. `MauiProgram.LoadApiClientOptions()` lê esse arquivo uma única vez, na inicialização (`FileSystem.OpenAppPackageFileAsync`), e é o único lugar do código que sabe onde esse arquivo está — nenhuma URL fica hardcoded em outro ponto do app.

**A URL da API não é um segredo** (Fase 13 §29) — só um endereço. O que nunca pode aparecer neste arquivo (nem em nenhum outro arquivo versionado do Mobile): a chave de assinatura do JWT, credenciais AWS, credenciais do PostgreSQL. `MobileSecretsScanTests` (`tests/FileSharing.Mobile.Tests/Security/`) verifica automaticamente que esse arquivo só tem essas duas chaves, e que nenhuma delas parece uma connection string/credencial.

---

## Segurança — confirmações explícitas

- **AWS credentials nunca estão no Mobile.** Nenhum tipo `Amazon.*`/AWSSDK existe em `FileSharing.Mobile`/`FileSharing.Mobile.Core` — o projeto nem referencia o pacote. A única coisa relacionada a S3 que o Mobile já viu é a presigned URL, devolvida pela própria API.
- **A chave de assinatura do JWT nunca está no Mobile** — só existe no lado da API (User Secrets/variável de ambiente, ver `docs/security.md`), nunca em nenhum artefato do cliente.
- **JWT armazenado via SecureStorage** (`ISecureStorageService`/`SecureStorageService`, Android Keystore) — nunca em `Preferences`, arquivo, log, código-fonte, ou recurso do APK.
- **Tokens/JWT/Authorization/presigned URLs nunca aparecem em log.** `FileSharingApiClient` não tem nenhuma dependência de `ILogger` — não existe caminho de código nele que pudesse logar o header `Authorization`. `SignalRNotificationService` loga só no nível de aviso/erro de conexão (nunca o token, nunca a URL do hub com query string). Confirmado automaticamente por `MobileSecretsScanTests` (varre o código-fonte por padrões de segredo) e pela ausência estrutural de qualquer `_logger.Log*(...)` que referencie essas propriedades.
- **Nenhum segredo no repositório.** Ver "Auditoria de segurança" no relatório final desta fase — busca teve escopo limitado ao repositório, nunca a `~/.aws`, variáveis de ambiente do sistema ou credenciais externas.

---

## UI: identidade visual

Tema azul (`#2563EB` como cor primária) sobre uma paleta neutra (`Resources/Styles/Colors.xaml`) — azul é reservado a ações primárias, progresso, seleção e estados ativos; o resto da interface é branco/cinza para preservar legibilidade e contraste. "Glass" é uma aproximação deliberadamente simples e performática (Fase 13 §8): `Border` com preenchimento branco translúcido + borda quase-branca, sem sombra (`Style x:Key="GlassCardBorder"` em `Resources/Styles/Styles.xaml`, aplicado diretamente a um `<Border>` em cada tela) — sem blur de verdade, que o .NET MAUI não oferece de forma nativa e multiplataforma, e que seria caro em aparelhos Android comuns. Nem sombra: um componente reutilizável anterior (`GlassCard`, um `ContentView` que repassava o conteúdo injetado via `ContentPresenter`) travava o app indefinidamente na tela de splash em hardware Android real assim que o conteúdo real era atribuído a ele — nunca em emulador, reproduzido e isolado por eliminação; a correção foi parar de repassar conteúdo por essa via e aplicar o estilo diretamente em `<Border>` inline em cada tela. O bottom sheet de ações do arquivo (`Views/FileDetailsPage.xaml`) é uma página modal comum (não um `Popup` de terceiros) estilizada para parecer um bottom sheet: fundo translúcido (scrim, toque fora fecha) + cartão ancorado embaixo com cantos superiores arredondados + animação de subida (`TranslateToAsync`) ao aparecer.

Nenhuma biblioteca de UI nova foi adicionada (nem `CommunityToolkit.Maui`, nem `SkiaSharp`) — tudo o pedido pela Fase 13 (cards translúcidos, bottom sheet, progresso real, ícones por tipo) foi alcançável com o MAUI puro já presente no projeto; ver o relatório final desta fase para o raciocínio completo de cada decisão de não adicionar pacote.

---

## Limitações conhecidas desta fase

- **Sem redirecionamento automático e centralizado para Login em qualquer 401.** Cada ViewModel trata seu próprio `ApiResult.ErrorType == Unauthorized` individualmente; não existe um "SessionGuard" central como o do Web (Fase 8/9) interceptando todo `FileSharingApiClient.SessionExpired` em um único lugar. Funcionalmente as chamadas continuam seguras (o usuário nunca vê dado de outra sessão), mas a UX de "sessão expirou, volte para o login" não é tão polida quanto no Web. Fica para uma fase futura.
- **Reconexão real de rede do SignalR não foi validada em campo** (sem dispositivo/emulador neste ambiente) — só o comportamento verificável sem uma rede real foi testado automaticamente. Ver "Validação em dispositivo real" no relatório final.
- **Bottom sheet é uma página modal estilizada, não um componente de bottom sheet nativo** (sem gesto de arrastar para fechar — só toque no scrim). Ver a seção de UI acima para o porquê dessa escolha.
- **iOS/macOS/Windows não foram exercitados nesta fase** — o projeto multi-targeta esses TFMs fora do Linux, mas todo o trabalho e validação desta fase foi feito exclusivamente para Android, que é o único alvo que compila neste ambiente de desenvolvimento.
- **Sem push notification do Android** — a notificação de download é inteiramente in-app via SignalR (banner + atualização do card), como pedido explicitamente pela Fase 13; uma notificação real do sistema operacional (fora do app aberto) fica para uma fase futura.
- **Sem deep link/App Link para o link de recuperação de senha** — o email aponta para a página Web; abrir esse link no navegador do celular não abre este app automaticamente. O usuário cola o token manualmente (ver "Recuperação de senha" acima). Configurar Android App Links (verificação de domínio via `assetlinks.json`) é uma melhoria possível para uma fase futura, fora do escopo desta.

## Como executar no Android

```bash
# Emulador (a partir do Android Studio ou avdmanager já configurado)
dotnet build src/FileSharing.Mobile -f net10.0-android -t:Run

# Ou, para gerar o APK sem instalar:
dotnet build src/FileSharing.Mobile -f net10.0-android
```

Pré-requisito: a API acessível na porta `5105` — via `dotnet run --project src/FileSharing.Api` (com `infrastructure/docker/docker-compose.yml` subindo só PostgreSQL + LocalStack) **ou**, desde a Etapa 14, via a Api já containerizada (`docker compose up -d`, ver `docs/development.md`) — os dois cenários expõem a mesma porta `5105` no host, então `Resources/Raw/appsettings.json` não muda entre eles. `10.0.2.2` continua sendo o alias do emulador para o `localhost` da máquina host em ambos os casos.

## Conectividade revisada na Etapa 14 (Docker + AWS)

Nenhuma mudança de código no Mobile — só uma confirmação de que a containerização da Api/Web não quebra nada:

- **O Mobile nunca é containerizado** (correto — app nativo, não um serviço de longa duração) e não foi alterado por esta etapa.
- **Emulador Android → Api containerizada:** idêntico ao cenário não-containerizado (`10.0.2.2:5105`), porque a Api continua expondo a mesma porta `5105` no host via `docker-compose.yml`. A única ressalva: se `AWS__PublicServiceURL` estiver configurado como `http://localhost:4566` (o padrão do `docker-compose.yml`, pensado para o **navegador** no host), uma presigned URL consumida pelo **emulador** precisaria de `10.0.2.2:4566` em vez de `localhost:4566` — para testar upload/download real a partir do emulador contra a stack Docker, ajuste `AWS__PublicServiceURL` no `docker-compose.yml` para `http://10.0.2.2:4566` antes de subir a stack (ver tabela de endereços em `docs/development.md`).
- **Dispositivo físico → Api containerizada:** IP da máquina host na rede local, igual ao cenário não-containerizado; mesma ressalva de `AWS__PublicServiceURL` acima, trocando `10.0.2.2` pelo IP real da máquina.
- **Produção (AWS, ver `docs/infrastructure.md`):** `BaseUrl`/`HubUrl` apontam para o domínio HTTPS público por trás do ALB — sem nenhum alias de emulador, sem `AWS:PublicServiceURL` (o S3 real já tem um único endpoint público). HTTPS é obrigatório; SignalR continua funcionando sobre WebSocket através do ALB (ver `docs/deployment.md`). Upload direto ao S3 via presigned URL não muda em nada — o Mobile nunca soube (nem precisa saber) se está falando com LocalStack ou S3 real.
- **Nada de App Links/Universal Links ou revogação de JWT foi adicionado nesta etapa** — ambos continuam deliberadamente fora de escopo, como já documentado na limitação acima.
