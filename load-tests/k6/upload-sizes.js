// Cenário "Upload" por tamanho de arquivo (Etapa 16, seções 7/8): mede separadamente
// metadata (Api→PostgreSQL), upload (Cliente→S3 direto, nunca pela Api) e complete
// (Api→PostgreSQL+HeadObject no S3) — nunca soma os três como um único número.
//
// 1 VU, poucas iterações sequenciais: o objetivo aqui é o custo POR TAMANHO, não concorrência
// (concorrência já é coberta por file-metadata.js/public-link-download.js). Rodar isolado por
// tamanho: k6 run -e FILE_SIZE_MB=50 -e ITERATIONS=5 load-tests/k6/upload-sizes.js
import http from "k6/http";
import { check, sleep } from "k6";
import { Trend } from "k6/metrics";
import { API_BASE, registerAndLogin, authHeaders } from "./lib/helpers.js";

const metadataDuration = new Trend("metadata_duration", true);
const s3PutDuration = new Trend("s3_put_duration", true);
const completeDuration = new Trend("complete_duration", true);

const FILE_SIZE_MB = Number(__ENV.FILE_SIZE_MB || 1);
const ITERATIONS = Number(__ENV.ITERATIONS || 5);
const SIZE_BYTES = FILE_SIZE_MB * 1024 * 1024;

export const options = {
  vus: 1,
  iterations: ITERATIONS,
};

// Gerado uma vez (setup, fora do timer de qualquer iteração) — nunca reconstruído a cada
// requisição, para que o benchmark meça o custo de rede/S3/PostgreSQL, não o custo de gerar a
// string em memória no próprio k6.
export function setup() {
  const { token } = registerAndLogin("uploadsize");
  const content = "A".repeat(SIZE_BYTES);
  return { token, content };
}

export default function (data) {
  const { token, content } = data;

  const initiateRes = http.post(
    `${API_BASE}/api/files/upload`,
    JSON.stringify({
      fileName: `bench-${FILE_SIZE_MB}mb.zip`,
      contentType: "application/zip",
      sizeBytes: SIZE_BYTES,
      isFolder: false,
    }),
    { ...authHeaders(token), tags: { name: `metadata_${FILE_SIZE_MB}mb` } }
  );
  check(initiateRes, { "initiate: 201": (r) => r.status === 201 });
  metadataDuration.add(initiateRes.timings.duration);
  if (initiateRes.status !== 201) return;

  const { fileId, uploadUrl } = initiateRes.json();

  const putRes = http.put(uploadUrl, content, {
    headers: { "Content-Type": "application/zip" },
    tags: { name: `s3_put_${FILE_SIZE_MB}mb` },
    timeout: "120s",
  });
  check(putRes, { "s3 put: 200": (r) => r.status === 200 });
  s3PutDuration.add(putRes.timings.duration);

  const completeRes = http.post(`${API_BASE}/api/files/${fileId}/complete`, null, {
    ...authHeaders(token),
    tags: { name: `complete_${FILE_SIZE_MB}mb` },
  });
  check(completeRes, { "complete: 200": (r) => r.status === 200 });
  completeDuration.add(completeRes.timings.duration);

  sleep(1);
}
