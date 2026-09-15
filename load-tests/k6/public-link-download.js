// Cenário "Public link" + "Download" (Etapa 16, seções 5/6): GET /api/public/files/{token} e
// GET /api/public/files/{token}/download.
//
// IMPORTANTE — este é o limitador mais restritivo do sistema: PublicFilesController usa uma
// única janela fixa GLOBAL e COMPARTILHADA (RateLimiterPolicyNames.PublicFiles), 30
// requisições/minuto para TODOS os clientes somados — não por IP, e (diferente da política Auth)
// o valor 30 está hardcoded em RateLimitingExtensions.cs, não vem de configuração. Não dá para
// elevar isso via variável de ambiente sem alterar código, e alterar uma política de segurança
// deliberada só para "destravar um benchmark" não é uma otimização baseada em evidência — é
// alterar a regra que está sendo medida. Por isso este script tem dois modos:
//
//   RUN_MODE=below_limit (padrão) — 1 requisição a cada ~3s (bem abaixo de 30/min), várias
//     iterações, para medir a latência REAL do código (lookup por AccessTokenHash + geração de
//     presigned URL + registro do Download) sem nenhuma rejeição por rate limit.
//   RUN_MODE=burst — 10 VUs por 90s (bem acima de 30/min), para provar/documentar que o rate
//     limiting rejeita o excesso corretamente. Aqui, 429 é o resultado ESPERADO — nunca
//     interpretado como falha de infraestrutura (ver docs/performance.md, seção Rate Limiting).
//
// setup() cria um usuário real, um arquivo real (upload completo via S3/LocalStack) e um link
// público real por VU esperado — nunca reaproveita o mesmo token repetidamente ao ponto de
// registrar um número artificial de downloads no mesmo arquivo (ver aviso da própria etapa:
// "não registrar tokens utilizados nos testes" — aqui entendido como "não deixar tokens de
// teste sobrando/reaproveitados fora deste dataset descartável", removido ao final via cleanup,
// ver docs/performance.md).
import http from "k6/http";
import { check, sleep } from "k6";
import { Trend, Counter } from "k6/metrics";
import {
  API_BASE,
  registerAndLogin,
  createAndCompleteFile,
  generatePublicLink,
} from "./lib/helpers.js";

const metadataDuration = new Trend("public_metadata_duration", true);
const downloadDuration = new Trend("public_download_duration", true);
const rateLimited429 = new Counter("rate_limited_429");

const RUN_MODE = __ENV.RUN_MODE || "below_limit";
const LINK_POOL_SIZE = Number(__ENV.LINK_POOL_SIZE || 20);

export const options =
  RUN_MODE === "burst"
    ? { vus: 10, duration: "90s" }
    : { vus: 1, duration: "60s" }; // below_limit: 1 VU, throttled pelo próprio sleep() abaixo

export function setup() {
  const { token } = registerAndLogin("publiclink");
  const accessTokens = [];
  for (let i = 0; i < LINK_POOL_SIZE; i++) {
    const result = createAndCompleteFile(token, 1024, "setup");
    if (result && result.completeOk) {
      const accessToken = generatePublicLink(token, result.fileId);
      if (accessToken) accessTokens.push(accessToken);
    }
  }
  return { accessTokens };
}

export default function (data) {
  if (data.accessTokens.length === 0) {
    sleep(1);
    return;
  }
  const accessToken = data.accessTokens[Math.floor(Math.random() * data.accessTokens.length)];

  const metaRes = http.get(`${API_BASE}/api/public/files/${accessToken}`, {
    tags: { name: "public_metadata" },
  });
  check(metaRes, { "metadata: 200 or 429": (r) => r.status === 200 || r.status === 429 });
  metadataDuration.add(metaRes.timings.duration);
  if (metaRes.status === 429) rateLimited429.add(1);

  const downloadRes = http.get(`${API_BASE}/api/public/files/${accessToken}/download`, {
    tags: { name: "public_download" },
  });
  check(downloadRes, { "download: 200 or 429": (r) => r.status === 200 || r.status === 429 });
  downloadDuration.add(downloadRes.timings.duration);
  if (downloadRes.status === 429) rateLimited429.add(1);

  if (RUN_MODE !== "burst") {
    // 2 requisições/iteração (metadata+download) — a 1 iteração/5s isso é 24 req/min, abaixo
    // do limite global compartilhado de 30/min (3s daria 40/min, acima do limite — corrigido
    // depois de observar 429 inesperado numa primeira execução, ver docs/performance.md).
    sleep(5);
  }
}
