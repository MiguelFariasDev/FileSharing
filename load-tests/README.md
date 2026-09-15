# Load tests (Etapa 16)

Scripts [k6](https://k6.io/) usados para a linha de base de performance documentada em `docs/performance.md`. Ver esse arquivo para os resultados medidos, metodologia completa e limitações — este README é só o "como rodar".

k6 não é uma dependência do projeto (não entra em `Directory.Packages.props`/nenhum `.csproj`) — é uma ferramenta externa de linha de comando. Instale com `https://k6.io/docs/get-started/installation/` ou baixe o binário standalone.

## Pré-requisito

Stack local rodando (`cd infrastructure/docker && docker compose up -d`) — todos os scripts apontam para `http://localhost:5105` (Api) por padrão, sobrescrevível via `-e API_BASE=...`.

## Scripts

| Script | O quê | Modo relevante |
|---|---|---|
| `k6/auth.js` | Register/Login/GET me | `-e RUN_MODE=capacity` (rate limit elevado, ver abaixo) ou `-e RUN_MODE=throttling` (limite real) |
| `k6/file-metadata.js` | initiate/mine/downloads (sem rate limit) | `-e STAGE=baseline\|stress\|spike\|endurance` |
| `k6/public-link-download.js` | Link público + download | `-e RUN_MODE=below_limit` (latência real) ou `-e RUN_MODE=burst` (prova o rate limit de 30/min) |
| `k6/upload-sizes.js` | Upload real via presigned URL, por tamanho | `-e FILE_SIZE_MB=1\|10\|50\|100\|200 -e ITERATIONS=5` |
| `zip-folder-benchmark.sh` | Tempo de ZIP de uma pasta + tamanho final | `./zip-folder-benchmark.sh [num_arquivos] [kb_por_arquivo]` |

## Elevando o rate limit de Auth para os testes de capacidade (`RUN_MODE=capacity`)

`RateLimiting:Auth:PermitLimit`/`RateLimiting:PasswordReset:PermitLimit` já são configuráveis (o código já lê de `IConfiguration`) — só não estavam conectados ao `docker-compose.yml`. Para medir a capacidade real do backend sem o limitador (20/min por IP, único IP nesta máquina) dominando o resultado:

```bash
cd infrastructure/docker
# Adiciona temporariamente ao serviço "api" do docker-compose.yml (ou exporte antes do up):
RATE_LIMITING_AUTH_PERMIT_LIMIT=100000 docker compose up -d api
```

Reverta depois (`docker compose up -d api` sem a variável) antes de rodar `-e RUN_MODE=throttling`, que precisa do limite real (20/min) para provar que o rate limiting funciona.

`GET /api/public/files/{token}`/`.../download` (`public-link-download.js`) **não** têm essa saída — o limite de 30/min ali é uma constante no código (`RateLimitingExtensions.cs`), deliberadamente não exposta como configuração (é uma decisão de segurança, não um parâmetro de performance) — por isso o script tem o modo `below_limit` em vez de precisar elevar nada.

## Exemplo de execução completa

```bash
k6 run -e RUN_MODE=capacity -e STAGE=baseline load-tests/k6/auth.js
k6 run -e STAGE=baseline load-tests/k6/file-metadata.js
k6 run -e RUN_MODE=below_limit load-tests/k6/public-link-download.js
k6 run -e RUN_MODE=burst load-tests/k6/public-link-download.js
k6 run -e FILE_SIZE_MB=10 -e ITERATIONS=5 load-tests/k6/upload-sizes.js
./load-tests/zip-folder-benchmark.sh 50 200
```

## Saída

`--out json=load-tests/results/<nome>.json` para salvar os dados brutos localmente (gitignored, `load-tests/results/` — nunca versionado; os números relevantes já ficam resumidos em `docs/performance.md`).

## Cleanup

Todo dado criado por estes scripts (usuários `*@loadtest.example`, arquivos, links) fica na base local (Postgres/LocalStack do Docker Compose) — nunca em produção (nenhum script aqui aceita apontar para uma URL de produção sem `-e API_BASE=...` explícito, e nenhuma etapa deste projeto até agora provisionou produção real). Para limpar: `docker compose down -v` remove os volumes locais inteiros (Postgres + LocalStack), ou rode `DELETE FROM files WHERE "OriginalFileName" LIKE 'loadtest%' OR "OriginalFileName" LIKE 'bench-%' OR "OriginalFileName" LIKE 'meta-only%';`/`DELETE FROM users WHERE "Email" LIKE '%@loadtest.example';` diretamente no Postgres local para um cleanup seletivo.
