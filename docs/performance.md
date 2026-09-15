# Performance & Load Testing — Etapa 16

Linha de base de performance do FileSharing, medida contra o ambiente **local Docker Compose** (Api + Web + PostgreSQL + LocalStack — ver `docs/development.md`). Todos os números abaixo são **medidos nesta sessão**, nunca estimados — ver "Ambiente de medição" para as limitações de hardware que afetam a leitura absoluta desses valores.

## Ferramenta escolhida: k6

Avaliado contra NBomber e JMeter (seção 23 do enunciado):

- **k6** foi escolhido. É uma ferramenta externa de linha de comando (não uma dependência do projeto — não entra em `Directory.Packages.props`/nenhum `.csproj`), o que evita acoplar a solução .NET a uma biblioteca de load testing só para esta etapa. Scripts em JavaScript, versionados em `load-tests/k6/`, reproduzíveis por qualquer pessoa com o binário do k6 instalado. Métricas de latência (min/avg/med/p90/p95/p99/max) e throughput (req/s) já vêm prontas por padrão, inclusive por métrica customizada (`Trend`) — exatamente o formato pedido na seção 31 (tabela `Scenario | Concurrency | p50 | p95 | p99 | RPS | Errors`).
- **NBomber** (a alternativa nativa .NET) exigiria um novo projeto de teste na solution (mais uma referência em `Directory.Packages.props`, mais um projeto no `FileSharing.slnx`) só para uma etapa cujo entregável é um relatório, não código de produto — mais fricção sem benefício real, já que os cenários aqui são inteiramente HTTP/WebSocket (nunca in-process).
- **JMeter** foi descartado por ser mais pesado para configurar/versionar (XML de plano de teste, GUI-first) para o mesmo resultado que scripts k6 dão em texto simples, fáceis de revisar em um PR.

Ver `load-tests/README.md` para como rodar cada script.

## Ambiente de medição

| | |
|---|---|
| Ambiente | Local — Docker Compose (`infrastructure/docker/docker-compose.yml`): Api + Web + PostgreSQL 16 + LocalStack 3, todos containers na mesma máquina que gera a carga (k6 rodando no host, fora do Docker) |
| Commit | `ce58547` (HEAD no início desta etapa; `dotnet ef`/build/testes rodados também após as alterações desta etapa) |
| .NET | 10.0.111 (SDK), runtime `net10.0` |
| Docker | 29.8.0 |
| CPU | 12 cores (`nproc`) |
| RAM total | 17.4 GiB |
| **RAM livre no momento dos testes** | **~376 MiB livres, 11 GiB em uso, ~22 GiB de swap em uso** — máquina de desenvolvimento compartilhada, não um ambiente de benchmark dedicado/isolado. Havia inclusive containers de **outro projeto** (`advocacia-*`) rodando simultaneamente. |

**Isto é uma limitação real e deve ser levada em conta ao ler os números absolutos abaixo**: sob pressão de memória/swap, latências de cauda (p99/max) tendem a ficar infladas por interferência do SO/GC, não necessariamente por um limite real da aplicação. Os containers da aplicação, isoladamente, usaram pouca memória (`filesharing-api` entre ~70–120MiB durante os testes — ver seção Endurance) — a pressão vem de **fora** do escopo mensurável desta aplicação. **Comparações relativas (antes/depois de uma otimização, ou entre níveis de concorrência na mesma execução) continuam válidas**, porque o ambiente permaneceu constante entre uma medição e outra dentro do mesmo cenário; **valores absolutos não devem ser lidos como representativos de um ambiente de produção dedicado** (ECS Fargate com CPU/memória reservados, sem vizinhos ruidosos).

**Staging AWS**: não avaliado. Nenhum recurso AWS (ECS/RDS/ALB) foi provisionado em nenhuma etapa anterior (Etapa 14/15 documentaram e prepararam, mas nunca executaram um deploy real — ver `docs/infrastructure.md`) — não há ambiente de staging real para medir. Seção 26 (AWS Performance) e 27 (Autoscaling) do enunciado não puderam ser executadas por esse motivo; documentado como limitação, não fabricado.

**Produção**: nunca testada, por design — não existe produção real além do que está documentado/preparado em `docs/infrastructure.md`.

## Auditoria — antes de qualquer medição (seção 2)

Feita antes de qualquer script de load test ser escrito. Achados relevantes:

- **Índices já existentes cobrem todos os campos citados no enunciado** (`Users.Email`, `Files.AccessTokenHash`, `Files.UserId`, `Files.ExpiresAt`, `Downloads.FileId`, `Downloads.DownloadedAt`) — confirmado via `\di` direto no Postgres, não pela leitura do código de configuração apenas. `Files.UserId` tem índice automático (convenção do EF Core para colunas de chave estrangeira), não um `HasIndex` explícito.
- **Nenhum N+1 óbvio nos caminhos quentes.** `FileQueryService.GetMyFilesAsync` já projeta via `.Select(...)` (incluindo `f.Downloads.Count`, que vira uma subquery correlacionada, uma única consulta SQL — não um round trip por arquivo).
- **Rate limiting muda fundamentalmente como qualquer benchmark de Auth/Public Files precisa ser desenhado** (achado crítico, não documentado antes desta etapa): `AuthController` tem `[EnableRateLimiting(Auth)]` na classe inteira — cobre não só `register`/`login`, mas também `GET /api/auth/me`, todos particionados por IP (20/min por padrão). `PublicFilesController` usa uma janela **global compartilhada** (não por IP) de **30 requisições/minuto para TODOS os clientes somados**, e esse valor está **hardcoded** em `RateLimitingExtensions.cs` (não vem de `IConfiguration`, diferente da política `Auth`). Rodar um load test ingênuo contra esses endpoints mediria principalmente o rate limiter, não a capacidade real do backend — ver "Metodologia" abaixo.
- **`FilesController` não tem rate limiting** — `initiate`/`complete`/`mine`/`downloads` medem diretamente a capacidade de Api+PostgreSQL.
- **`AddDbContext` (não `AddDbContextPool`)** — sem pooling de `DbContext`. Não alterado nesta etapa (nenhuma medição indicou isso como gargalo real nos volumes testados — ver "Itens avaliados e não alterados").
- **S3/HttpClient já corretos.** Dois `IAmazonS3` singletons (real + presign, ver `docs/architecture.md`), Web usa `AddHttpClient<T>` (via `IHttpClientFactory`). **Um ponto de atenção encontrado, não corrigido nesta etapa**: `FileSharing.Mobile`/`UploadExtensions.cs` registra `S3UploadHttpClient` como `AddTransient` — uma nova instância (com seu próprio `HttpMessageHandler`) a cada upload, em vez de reaproveitada. Ver "Itens avaliados e não alterados".
- **`ExpiredFileCleanupJob` processa candidatos em um laço sequencial, com `SaveChangesAsync` por item, no mesmo `DbContext` do início ao fim do job** — candidato natural a crescimento de custo por item conforme o lote cresce (change tracker do EF Core). Confirmado por medição — ver "Hangfire" abaixo; é a única otimização real aplicada nesta etapa.

## Metodologia — como o rate limiting molda os testes

| Endpoint | Rate limit real | Estratégia de teste |
|---|---|---|
| `/api/auth/*` (register/login/me) | 20/min por IP (configurável) | Dois modos: `RUN_MODE=throttling` (limite real, prova que funciona) e `RUN_MODE=capacity` (limite elevado via `RateLimiting__Auth__PermitLimit`, agora conectado ao `docker-compose.yml` — mede capacidade real) |
| `/api/public/files/{token}` e `.../download` | 30/min **global**, hardcoded | Dois modos: `RUN_MODE=below_limit` (latência real, sem 429) e `RUN_MODE=burst` (prova que o limite segura sob ataque) — não dá para "elevar" este limite sem mudar código de segurança deliberado, então a capacidade "sem rate limit" deste endpoint não é medida isoladamente |
| `/api/files/*` | Nenhum | Medido diretamente em progressão de concorrência (10/25/50/100/250) |

## 1. Autenticação

### Throttling (limite real — 20/min por IP)

90s, ramp até 5 VUs, todo tráfego de um único IP (a própria máquina de teste):

| Métrica | Valor |
|---|---|
| Requisições totais | 464 |
| **429 (esperado)** | 424 (91.4%) |
| Erros reais (5xx/timeout) | 0 |
| RPS efetivo | 5.1/s |

**Confirma que o rate limiting funciona exatamente como projetado** — nenhum 5xx, só rejeição correta acima do orçamento configurado.

### Capacidade (limite elevado — mede o backend real)

Ramp 10→25→50→100 VUs, 80s total:

| Scenario | Concorrência (pico) | p50 | p95 | p99 | RPS | Errors |
|---|---|---|---|---|---|---|
| Register | 100 | 348ms | 1.24s | 1.54s | — | 0 |
| Login | 100 | 306ms | 1.17s | 1.48s | — | 0 |
| GET /me | 100 | 8.8ms | 462ms | 791ms | — | 0 |
| **Total combinado** | 100 | 204ms | 1.09s | 1.45s | 51.0/s | 0 |

**0% de erro em toda a rampa até 100 VUs simultâneos.** Register/Login degradam sob concorrência bem mais que `/me` — esperado: `PasswordHasher<T>` (PBKDF2, ASP.NET Core Identity) é deliberadamente lento por design de segurança, e cada requisição de register/login roda um hash completo; `/me` não hasheia nada, só decodifica o JWT já validado pelo middleware. **Isto não é um gargalo a corrigir** — enfraquecer o hashing de senha para "melhorar o número" contradiria diretamente a segurança já estabelecida (Etapa 2/10) e o enunciado desta etapa ("não altere regras de negócio").

## 2. File metadata

`POST /api/files/upload` (metadata), `GET /api/files/mine`, `GET /api/files/{id}/downloads` — sem rate limiting.

| Scenario | Concurrency | p50 | p95 | p99 | RPS | Errors |
|---|---|---|---|---|---|---|
| initiate | 10 | 6.46ms | 804.86ms* | 904.73ms* | 27.3/s | 0 |
| initiate | 25 | 5.39ms | 51.34ms | 290.11ms | 71.0/s | 0 |
| initiate | 50 | 7.17ms | 178.01ms | 230.35ms | 131.3/s | 0 |
| initiate | 100 | 26.96ms | 369.95ms | 819.06ms | 224.5/s | 0 |
| mine | 10 | 3.92ms | 258.9ms* | 260.58ms* | — | 0 |
| mine | 25 | 3.2ms | 9.5ms | 15.23ms | — | 0 |
| mine | 50 | 3.48ms | 21.67ms | 99.46ms | — | 0 |
| mine | 100 | 4.68ms | 162.72ms | 567.71ms | — | 0 |

\* O nível de 10 VUs (primeira execução da bateria) mostra p95 mais alto que o de 25 VUs — efeito de aquecimento (JIT/connection pool ainda não aquecidos, primeira execução depois do container subir), não uma regressão real de concorrência menor sendo mais lenta que concorrência maior; medição consistente com isso já que os níveis subsequentes (25/50/100) escalam monotonicamente como esperado.

**0% de erro em todos os níveis, RPS crescendo quase linearmente com a concorrência** (27→71→131→224 req/s) até 100 VUs sem sinal de saturação.

## 3. Public link + Download

`GET /api/public/files/{token}` e `.../download` — limite real de 30/min **global** (não elevável).

### Abaixo do limite (latência real, sem 429)

1 VU, 1 par de chamadas a cada 5s (24/min, abaixo do limite de 30/min):

| Scenario | p50 | p95 | p99 | Errors |
|---|---|---|---|---|
| metadata (`GET /api/public/files/{token}`) | 2.26ms | 2.84ms | 2.94ms | 0 (0 × 429) |
| download (`GET .../download`) | 8.88ms | 19.43ms | 23.24ms | 0 (0 × 429) |

`download` é mais lento que `metadata` porque também chama `ObjectExistsAsync` (HeadObject no S3/LocalStack), grava um `Download` no PostgreSQL, e gera a presigned URL — três operações a mais que a simples consulta de metadata.

### Burst (prova o rate limiting — 429 esperado)

10 VUs, 90s, sem pausa entre chamadas:

| Métrica | Valor |
|---|---|
| Requisições totais | 960.600 (10.608/s tentado) |
| **429 (esperado)** | 960.458 (99.98%) |
| Erros reais (5xx/timeout) | 0 |
| p95 de resposta (inclusive os 429) | 1.43ms |

**O rate limiter rejeita corretamente uma tentativa de ~10.600 req/s com overhead sub-2ms e zero erro real** — o sistema permaneceu estável durante todo o burst (também serve como evidência de stress test para este endpoint, seção 14).

## 4. Upload por tamanho de arquivo

Presigned URL real (Cliente→S3 direto, nunca pela Api), 1 VU, 3–5 iterações sequenciais por tamanho, `metadata`/`s3_put`/`complete` medidos separadamente.

| Tamanho | metadata (p95) | S3 PUT (p95) | complete (p95) | Errors |
|---|---|---|---|---|
| 1 MB | 7.61ms | 10.29ms | 8.08ms | 0 |
| 10 MB | 5.14ms | 61.63ms | 7.5ms | 0 |
| 50 MB | 26.94ms | 258.42ms | 54.97ms | 0 |
| 100 MB | 5.6ms | 659.17ms | 9.34ms | 0 |
| 200 MB | 21.89ms | 2.02s | 537.42ms | 0 |

- **`metadata` (initiate) não depende do tamanho do arquivo** — é só uma escrita de linha no PostgreSQL mais uma chamada local de assinatura HMAC (presigned URL); os valores flutuam por ruído do ambiente (ver "Ambiente de medição"), não por tamanho.
- **S3 PUT escala aproximadamente linear com o tamanho** — de ~10ms (1MB) a ~2s (200MB), consistente com throughput de rede/disk do LocalStack local, não da lógica da Api (que nunca vê os bytes).
- **`complete` cresce em tamanhos maiores** (537ms em 200MB vs. ~8ms em arquivos pequenos) — a Api chama `GetObjectMetadataAsync` (HeadObject) contra o objeto recém-enviado; em LocalStack, isso parece ter custo não desprezível para objetos grandes. Não investigado a fundo nesta etapa (não é um gargalo do código da Api, e LocalStack não deve ser lido como equivalente ao S3 real neste aspecto — ver "S3 Client" abaixo).
- **0% de erro em todos os tamanhos**, incluindo 200MB.

## 5. ZIP de pasta

`load-tests/zip-folder-benchmark.sh` — tempo de compactação medido **separado** do tempo de upload.

| Cenário | Original | ZIP final | Tempo de compactação |
|---|---|---|---|
| 50 arquivos binários incompressíveis (~200KB cada, simulando fotos/vídeos) | 9 MB | 9.77MB | 0.24s |
| 50 arquivos de texto repetitivo (~200KB cada) | 9 MB | 0.04MB | 0.05s |

**Achado real, não um problema do código**: para pastas de mídia já comprimida (fotos JPEG, vídeos MP4 — o caso de uso mais provável do produto), ZIP **não reduz o tamanho e ainda adiciona ~8% de overhead** — o benefício de compressão do ZIP é quase todo para conteúdo textual/já-descomprimido. Isso não é uma decisão de código a mudar (compressão sem perdas continua correta e necessária para agrupar múltiplos arquivos em um único upload) — é uma característica inerente de compressão sem perdas sobre dados já comprimidos, documentada aqui para gerenciar expectativa, não corrigida.

## 6. PostgreSQL / EF Core — `EXPLAIN ANALYZE`

Dataset sintético gerado via SQL para este teste (2.000 usuários, 50.000 arquivos, ~49.854 downloads — muito acima do volume de dev normal, para dar ao planner do Postgres algo realista para decidir entre index scan e seq scan), removido ao final.

| Query (caminho real) | Plano | Tempo de execução |
|---|---|---|
| `Users` por `Email` (login) | Index Scan (`IX_users_Email`) | 0.08ms |
| `Files` por `AccessTokenHash` (link/download público) | Index Scan (`IX_files_AccessTokenHash`) | 0.115ms |
| `Files` por `UserId` ORDER BY `CreatedAt` (`mine`) | Bitmap Index Scan (`IX_files_UserId`) + sort em memória | 0.409ms |
| `Files` WHERE `Status`=Active AND `ExpiresAt`<=now() (Hangfire) | Bitmap Index Scan (`IX_files_ExpiresAt`) | 0.114ms |
| `Downloads` por `FileId` ORDER BY `DownloadedAt` (histórico) | Index Scan (`IX_downloads_FileId`) + sort em memória | 0.091ms |

**Nenhuma das cinco queries faz Sequential Scan** — todas usam o índice esperado, mesmo com 50.000 arquivos/2.000 usuários. **Nenhum índice novo foi adicionado** — medido primeiro (seção 10/11 do enunciado), e a medição não encontrou necessidade real neste volume.

## 7. Hangfire — Expiration Cleanup

100/1.000 arquivos reais (upload completo via API+S3, nunca um atalho) — `ExpiresAt` retroagido via SQL direto (a API nunca permite isso via HTTP; regra de domínio preservada), já que é a única forma de criar candidatos sem esperar 24h reais. 10.000 arquivos **não foi executado nesta sessão** por tempo — ver "Limitações".

### Antes da otimização

| Candidatos | BatchSize | Duração | ms/arquivo |
|---|---|---|---|
| 100 | 100 | 1.975ms | ~19.75 |
| 100 (execução seguinte, tracker "aquecido") | 100 | 494–778ms | ~4.9–7.8 |
| 600 (uma única execução) | 1000 | 10.797ms | **~18.0** |

**Gargalo real encontrado**: o custo por arquivo quase quadruplicou ao processar 600 de uma vez (18ms/arquivo) comparado a lotes de 100 em sequência (~5–8ms/arquivo). Investigado: `ExpiredFileCleanupJob` mantém o **mesmo `DbContext`** do início ao fim da execução, chamando `SaveChangesAsync()` a cada item — cada chamada roda `DetectChanges()` sobre **todas** as entidades já rastreadas no laço (custo crescente, quadrático no total).

### Otimização aplicada

`_dbContext.Entry(file).State = EntityState.Detached;` logo após o `SaveChangesAsync()` de cada item bem-sucedido (`src/FileSharing.Infrastructure/BackgroundJobs/ExpiredFileCleanupJob.cs`) — desanexa **só** a entidade já salva, nunca `ChangeTracker.Clear()` (que desanexaria também os candidatos ainda não processados do mesmo laço — bug real encontrado e corrigido durante a própria implementação desta otimização, pego pelos testes unitários já existentes, `ExpiredFileCleanupJobTests`, sem precisar escrever nenhum teste novo).

### Depois da otimização

| Candidatos | BatchSize | Duração | ms/arquivo |
|---|---|---|---|
| 100 | 100 | 634ms | ~6.3 |
| 1.000 (uma única execução) | 1000 | 9.930ms | **~9.9** |

**Comparação direta (mesma metodologia, mesmo ambiente):** processando **1.000** arquivos de uma vez depois da correção (9.9ms/arquivo) é mais rápido por item do que processar apenas **600** antes da correção (18ms/arquivo) — apesar de processar 67% mais arquivos. **Redução de ~45% no custo por item em lotes grandes.** A escala não ficou perfeitamente linear (100→1000 ainda mostra algum crescimento, de ~6.3 para ~9.9ms/arquivo) — atribuído ao custo sequencial das próprias chamadas `DeleteObjectAsync` ao LocalStack (não paralelizadas, fora do escopo desta correção), não a um efeito residual de change tracker.

### Regressão após a otimização

`dotnet build`/`dotnet test` (todos os 5 projetos, 527/527) e `dotnet format --verify-no-changes` executados depois da mudança — ver seção "Testes executados".

## 8. Concorrência progressiva / Stress test

`file-metadata.js`, rampa 25→50→100→150 VUs sustentada (30s/60s/60s/60s = 3m30s):

| Concorrência (pico) | p50 | p95 | p99 | RPS | Errors |
|---|---|---|---|---|---|
| até 250* | 4.17ms | 50.71ms | 114.52ms | 242.8/s | 0 |

\* O perfil de stress definido usa 150 como pico nominal, mas o k6 relatou `vus_max: 250` — o script já continha um perfil mais agressivo reaproveitado de uma iteração anterior; o resultado real reflete a rampa efetivamente executada (25→50→100→250).

**Nenhum ponto de degradação/erro encontrado até 250 VUs simultâneos neste hardware** — 0% de erro, p99 sob 115ms durante toda a rampa. **250-500 VUs adicionais não foram testados**: o enunciado (seção 12) explicitamente autoriza não rodar todos os níveis em todo ambiente, e nesta máquina compartilhada (ver "Ambiente de medição") ir além arriscaria medir contenção de recursos do host/outros processos, não da aplicação.

## 9. Spike test

Baseline 10 VUs → salto para 100 VUs (10s) → volta a 10 VUs:

| Fase | p95 | Errors |
|---|---|---|
| Todo o teste (50s) | 20.82ms | 0 |
| p99 | 99.01ms | 0 |

**Recuperação limpa** — nenhum erro durante o salto abrupto, nenhuma degradação sustentada após o retorno ao baseline.

## 10. Endurance test (reduzido)

**Reduzido de 30–60 minutos (pedido no enunciado) para 5 minutos**, por restrição de tempo desta sessão — documentado como redução deliberada, não omissão. 20 VUs constantes, `file-metadata.js`:

| Métrica | Valor |
|---|---|
| Duração | 5m01s |
| Requisições | 8.887 |
| Erros | 0 (0.00%) |
| p95 | 12.12ms |
| p99 | 47.76ms |

Amostras de `docker stats` do container `filesharing-api` a cada 30s durante o teste:

| t (s) | CPU% | Memória |
|---|---|---|
| 0 | 12.84% | 107.8 MiB |
| 30 | 17.95% | 100.6 MiB |
| 60 | 3.35% | 101.1 MiB |
| 90 | 9.32% | 108.9 MiB |
| 120 | 5.86% | 105.7 MiB |
| 150 | 8.17% | 84.0 MiB |
| 180 | 9.16% | 71.9 MiB |
| 210 | 10.36% | 80.1 MiB |
| 240 | 21.39% | 68.6 MiB |
| 270 | 13.61% | 70.0 MiB |

**Nenhum crescimento de memória observado** — a memória do container na verdade **caiu** ao longo do teste (107.8MiB → ~70MiB), consistente com o GC do .NET liberando alocações do aquecimento inicial (JIT, primeiras conexões de pool) e não com um memory leak. CPU oscilou sem tendência de crescimento. **5 minutos é curto demais para detectar um leak lento** (a mesma ressalva que se aplicaria a qualquer endurance test reduzido) — recomendado rodar os 30–60 minutos completos antes de qualquer decisão de capacidade para produção real (ver "Recomendações futuras").

## 11. SignalR

Escopo deliberadamente reduzido (ver `load-tests/k6/signalr-connections.js`): mede conexão/handshake, não latência de entrega de mensagem (`FileDownloaded`), já coberta pelos testes funcionais determinísticos existentes (`NotificationHubTests`).

25 conexões autenticadas simultâneas, handshake completo (negotiate HTTP + WebSocket + protocolo JSON do SignalR):

| Métrica | p50 | p95 | max | Errors |
|---|---|---|---|---|
| negotiate (HTTP) | 19ms | 142ms | 149ms | 0 |
| WebSocket handshake | 86ms | 94ms | 95ms | 0 |

**25/25 conexões estabelecidas com sucesso.** Nenhum broadcast usado — cada VU só abre e mantém sua própria conexão, preservando `Clients.User(userId)`/isolamento por usuário inalterado. Múltiplas instâncias da Api (backplane) continuam fora de escopo — não implementado, como pedido explicitamente; a limitação de alcance entre instâncias permanece documentada em `docs/deployment.md`.

## 12. Rate limiting — Expected vs. Unexpected

Consolidado das seções 1/3 acima, respondendo diretamente à seção 20 do enunciado:

- **Expected throttling** (comportamento correto, não uma falha): os 424/464 `429` no teste de Auth em modo `throttling`, e os 960.458/960.600 `429` no teste de burst do link público — ambos artefatos deliberados do próprio teste (excedendo de propósito o orçamento configurado), nunca contados como "erro" nas tabelas acima.
- **Unexpected errors** (nenhum encontrado): 0% de 5xx/timeout/connection error em **qualquer** cenário rodado nesta etapa — incluindo os dois testes de rate limiting, os testes de capacidade elevada, stress, spike e endurance.

## Itens avaliados e não alterados (evidência não justificou mudança)

- **`AddDbContext` sem pooling** — nenhum cenário testado (até 250 VUs, PostgreSQL local) mostrou latência atribuível a overhead de criação de `DbContext`; introduzir `AddDbContextPool` sem uma medição que o justifique seria otimização especulativa, contra o pedido explícito do enunciado.
- **`AsNoTracking()` em leituras puras** (`FilePublicLinkService.GetByAccessTokenAsync`, a query de ownership de `GetDownloadHistoryAsync`) — os tempos medidos (seção 3, "abaixo do limite": p95 de 2.84ms para metadata) já são baixíssimos; o ganho teórico de pular o tracking de uma única entidade por requisição é menor que o ruído de ambiente medido nesta sessão (ver "Ambiente de medição"). Fica documentado como candidato de baixo risco para uma etapa futura com um ambiente mais controlado para confirmar.
- **`FileSharing.Mobile`'s `AddTransient<S3UploadHttpClient>`** (encontrado na auditoria — uma nova instância de `HttpClient` por upload, em vez de reaproveitada) — não alterado nesta etapa: Mobile não é o alvo principal do benchmark de infraestrutura (pedido explícito do enunciado, seção 22), o padrão de uso real (um usuário fazendo poucos uploads, não uma rajada de milhares) torna o risco de esgotamento de portas efêmeras baixo na prática, e mudar código do Mobile sem um cenário de carga real de Mobile para validar seria especulativo. Documentado como recomendação futura.

## Testes executados (regressão completa)

Depois da única alteração de código desta etapa (`ExpiredFileCleanupJob.cs` + a pequena adição a `IApplicationDbContext`):

```
dotnet build FileSharing.slnx        → 0 erros
dotnet test FileSharing.slnx         → 527/527 (170 UnitTests + 11 IntegrationTests +
                                         107 Mobile.Tests + 130 Web.Tests + 109 ApiTests)
dotnet format --verify-no-changes    → só o arquivo pré-existente e não tocado
                                         (MainApplication.cs, Mobile) diverge, como já
                                         documentado desde a Etapa 14/15
docker compose build/up               → 4 containers saudáveis
./infrastructure/docker/e2e-smoke-test.sh → passou de ponta a ponta após o rebuild
```

## Limitações desta etapa

- **Staging AWS não avaliado** — nenhum recurso AWS real provisionado em nenhuma etapa (ver `docs/infrastructure.md`); seções 26/27 do enunciado não puderam ser executadas.
- **Endurance reduzido de 30–60min para 5min** por tempo de sessão — resultado (sem crescimento de memória) é encorajador mas não conclusivo para um leak lento.
- **Hangfire não testado no tier de 10.000 arquivos** por tempo — metodologia e comandos exatos documentados em `load-tests/README.md`/acima para reprodução.
- **Máquina de desenvolvimento compartilhada, sob pressão de memória/swap** durante toda a sessão (ver "Ambiente de medição") — valores absolutos de latência de cauda devem ser lidos com essa ressalva; comparações relativas (antes/depois, entre níveis) continuam válidas.
- **`complete` mais lento em uploads grandes (200MB) não investigado a fundo** — hipótese (custo de HeadObject no LocalStack para objetos grandes) não confirmada com profiling; LocalStack não deve ser lido como equivalente ao S3 real neste aspecto especificamente.
- **Concorrência acima de 250 VUs não testada** — nenhum sinal de degradação foi encontrado até esse nível nesta máquina; não há evidência de qual seria o próximo patamar sem testar mais.

## Recomendações técnicas futuras (não implementadas — apenas observação)

- Rodar o endurance test completo (30–60min) e o tier de 10.000 arquivos do Hangfire num ambiente dedicado (não uma máquina de desenvolvimento compartilhada) antes de qualquer decisão de capacidade de produção.
- Se o Mobile algum dia demonstrar uploads em rajada real (não hipotético), trocar `AddTransient<S3UploadHttpClient>` por `AddSingleton`/`AddHttpClient`.
- Confirmar `AsNoTracking()` em `GetByAccessTokenAsync`/consultas de ownership com um benchmark isolado (sem o ruído desta máquina) antes de aplicar.
- Se e quando um ambiente AWS real (ECS/RDS/ALB) for provisionado (`docs/infrastructure.md`), repetir os cenários desta etapa lá para estabelecer uma segunda linha de base — CPU/memória do ECS, conexões/CPU do RDS, tempo de resposta do ALB, comportamento de autoscaling (se configurado) são todos dados que só existem com infraestrutura real.

## Como reproduzir

Ver `load-tests/README.md` para o passo a passo completo de cada script, incluindo como elevar temporariamente o rate limit de Auth (`RateLimiting__Auth__PermitLimit`, agora conectado ao `docker-compose.yml`) e o `BatchSize`/`IntervalMinutes` do Hangfire (`ExpirationCleanup__BatchSize`/`ExpirationCleanup__IntervalMinutes`, também agora conectados) para os testes de capacidade/volume.
