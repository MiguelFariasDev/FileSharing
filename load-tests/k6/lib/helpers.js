// Funções compartilhadas pelos scripts de load test (Etapa 16). Nunca loga token/JWT/senha —
// só o necessário para as chamadas HTTP funcionarem, e nunca console.log de payloads sensíveis
// (k6 imprimiria isso no output do próprio processo de teste, que pode acabar em CI/artifacts).
import http from "k6/http";
import { check } from "k6";

export const API_BASE = __ENV.API_BASE || "http://localhost:5105";
export const WEB_BASE = __ENV.WEB_BASE || "http://localhost:5287";

// Senha de teste fixa e claramente não-real — nunca uma senha de usuário real, nunca reaproveitada
// fora deste diretório de load tests.
export const TEST_PASSWORD = "LoadTest!2026Perf";

export function randomEmail(prefix) {
  // __ITER só existe dentro da função default (por VU) — indisponível em setup()/teardown(),
  // que rodam fora do contexto de uma iteração; Date.now()+random já garante unicidade sozinho.
  const iter = typeof __ITER !== "undefined" ? __ITER : "setup";
  const vu = typeof __VU !== "undefined" ? __VU : 0;
  return `${prefix}_${vu}_${iter}_${Date.now()}_${Math.floor(Math.random() * 1e9)}@loadtest.example`;
}

export function registerAndLogin(prefix) {
  const email = randomEmail(prefix);
  const registerRes = http.post(
    `${API_BASE}/api/auth/register`,
    JSON.stringify({ email, password: TEST_PASSWORD }),
    { headers: { "Content-Type": "application/json" }, tags: { name: "register" } }
  );
  check(registerRes, { "register: 201": (r) => r.status === 201 });

  const loginRes = http.post(
    `${API_BASE}/api/auth/login`,
    JSON.stringify({ email, password: TEST_PASSWORD }),
    { headers: { "Content-Type": "application/json" }, tags: { name: "login" } }
  );
  check(loginRes, { "login: 200": (r) => r.status === 200 });

  const token = loginRes.status === 200 ? loginRes.json("accessToken") : null;
  return { email, token };
}

export function authHeaders(token) {
  return { headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" } };
}

// Cria e finaliza um upload real (metadata → PUT direto na presigned URL → complete) — o mesmo
// fluxo de três passos do produto real, nunca um atalho que pule o S3/LocalStack. `sizeBytes`
// controla o conteúdo gerado; `isFolder`/`contentType` seguem as mesmas regras de validação já
// existentes (FileTypePolicy) — application/zip é sempre aceito e cobre tanto "arquivo zip
// individual" quanto "pasta comprimida" para fins de benchmark de tamanho.
export function createAndCompleteFile(token, sizeBytes, tagPrefix) {
  const content = "A".repeat(sizeBytes);

  const initiateRes = http.post(
    `${API_BASE}/api/files/upload`,
    JSON.stringify({
      fileName: `loadtest-${sizeBytes}.zip`,
      contentType: "application/zip",
      sizeBytes,
      isFolder: false,
    }),
    { ...authHeaders(token), tags: { name: `${tagPrefix}_initiate` } }
  );
  check(initiateRes, { "initiate: 201": (r) => r.status === 201 });
  if (initiateRes.status !== 201) return null;

  const { fileId, uploadUrl } = initiateRes.json();

  const putRes = http.put(uploadUrl, content, {
    headers: { "Content-Type": "application/zip" },
    tags: { name: `${tagPrefix}_s3_put` },
  });
  check(putRes, { "s3 put: 200": (r) => r.status === 200 });

  const completeRes = http.post(
    `${API_BASE}/api/files/${fileId}/complete`,
    null,
    { ...authHeaders(token), tags: { name: `${tagPrefix}_complete` } }
  );
  check(completeRes, { "complete: 200": (r) => r.status === 200 });

  return { fileId, completeOk: completeRes.status === 200 };
}

export function generatePublicLink(token, fileId) {
  const res = http.post(`${API_BASE}/api/files/${fileId}/link`, null, {
    ...authHeaders(token),
    tags: { name: "generate_link" },
  });
  check(res, { "link: 200": (r) => r.status === 200 });
  return res.status === 200 ? res.json("accessToken") : null;
}
