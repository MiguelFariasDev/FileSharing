// Cenário "File metadata" (Etapa 16, seção 4): POST /api/files/upload (metadata only — sem PUT
// real ao S3 aqui, isso é o cenário upload-sizes.js), POST /api/files/{id}/complete, GET
// /api/files/mine, GET /api/files/{id}/downloads. FilesController não tem rate limiting (ver
// docs/performance.md, auditoria) — mede diretamente a capacidade de Api+PostgreSQL sem
// interferência de rate limiter.
//
// Login é feito uma vez por VU (cacheado em uma variável de módulo — cada VU do k6 é um runtime
// JS isolado, então isto persiste entre iterações do mesmo VU sem precisar de setup() por VU).
import http from "k6/http";
import { check, sleep } from "k6";
import { Trend } from "k6/metrics";
import { API_BASE, registerAndLogin, authHeaders } from "./lib/helpers.js";

const initiateDuration = new Trend("initiate_duration", true);
const mineDuration = new Trend("mine_duration", true);
const downloadsHistoryDuration = new Trend("downloads_history_duration", true);

const STAGE = __ENV.STAGE || "baseline";

const STAGE_PROFILES = {
  baseline: [
    { duration: "20s", target: 10 },
    { duration: "20s", target: 25 },
    { duration: "20s", target: 50 },
    { duration: "20s", target: 100 },
  ],
  stress: [
    { duration: "30s", target: 25 },
    { duration: "60s", target: 50 },
    { duration: "60s", target: 100 },
    { duration: "60s", target: 250 },
  ],
  spike: [
    { duration: "20s", target: 10 },
    { duration: "10s", target: 100 },
    { duration: "20s", target: 10 },
  ],
  endurance: [{ duration: __ENV.ENDURANCE_DURATION || "10m", target: 20 }],
};

// FIXED_VUS permite rodar um nível de concorrência isolado e constante (10/25/50/100 — seção 12)
// em vez de uma rampa contínua, para preencher a tabela de resultados com uma linha por nível.
export const options = __ENV.FIXED_VUS
  ? { vus: Number(__ENV.FIXED_VUS), duration: __ENV.FIXED_DURATION || "20s" }
  : { stages: STAGE_PROFILES[STAGE] };

let cachedToken = null;
let ownedFileId = null;

export default function () {
  if (!cachedToken) {
    const { token } = registerAndLogin("meta");
    cachedToken = token;
  }
  if (!cachedToken) {
    sleep(1);
    return;
  }

  // initiate (metadata only — nunca envia bytes reais aqui, esse custo é isolado em
  // upload-sizes.js)
  const initiateRes = http.post(
    `${API_BASE}/api/files/upload`,
    JSON.stringify({
      fileName: "meta-only.zip",
      contentType: "application/zip",
      sizeBytes: 1024,
      isFolder: false,
    }),
    { ...authHeaders(cachedToken), tags: { name: "initiate" } }
  );
  check(initiateRes, { "initiate: 201": (r) => r.status === 201 });
  initiateDuration.add(initiateRes.timings.duration);

  // mine (lista — sempre existe pelo menos o arquivo recém-criado acima, mesmo ainda pendente)
  const mineRes = http.get(`${API_BASE}/api/files/mine`, {
    ...authHeaders(cachedToken),
    tags: { name: "mine" },
  });
  check(mineRes, { "mine: 200": (r) => r.status === 200 });
  mineDuration.add(mineRes.timings.duration);

  if (ownedFileId) {
    const downloadsRes = http.get(`${API_BASE}/api/files/${ownedFileId}/downloads`, {
      ...authHeaders(cachedToken),
      tags: { name: "downloads_history" },
    });
    check(downloadsRes, { "downloads history: 200": (r) => r.status === 200 });
    downloadsHistoryDuration.add(downloadsRes.timings.duration);
  } else if (initiateRes.status === 201) {
    ownedFileId = initiateRes.json("fileId");
  }

  sleep(1);
}
