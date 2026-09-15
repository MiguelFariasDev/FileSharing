// Cenário "Autenticação" (Etapa 16, seção 3): Register, Login, GET /api/auth/me.
//
// IMPORTANTE sobre rate limiting: AuthController tem [EnableRateLimiting(Auth)] na classe
// inteira (register/login/me), 20 req/min POR IP por padrão (RateLimiting:Auth:PermitLimit).
// Como este script roda de uma única máquina, todo tráfego compartilha um único IP de origem —
// rodar isto contra os limites padrão de produção mede principalmente o rate limiter, não a
// capacidade real do backend. Por isso este arquivo tem dois modos, selecionados por RUN_MODE:
//
//   RUN_MODE=capacity (padrão) — pressupõe que RateLimiting__Auth__PermitLimit foi
//     temporariamente elevado no container (ver docs/performance.md, "Como reproduzir") para
//     medir a capacidade real do código (hash de senha, JWT, EF Core) sem o limitador
//     interferindo. NUNCA rode este modo contra um ambiente com o rate limit padrão sem
//     esperar uma avalanche de 429 dominando os números.
//   RUN_MODE=throttling — roda contra os limites REAIS (20/min) especificamente para provar
//     que o rate limiting funciona; poucos VUs, e 429 é o resultado ESPERADO, não um erro.
//
// Uso:
//   k6 run -e RUN_MODE=capacity   -e STAGE=baseline load-tests/k6/auth.js
//   k6 run -e RUN_MODE=throttling load-tests/k6/auth.js
import http from "k6/http";
import { check, sleep } from "k6";
import { Trend, Counter } from "k6/metrics";
import { API_BASE, TEST_PASSWORD, randomEmail, authHeaders } from "./lib/helpers.js";

// Trends dedicados por endpoint — o http_req_duration agregado do k6 mistura os três endpoints
// num único percentil, o que não serve para a tabela de resultados (Etapa 16, seção 31), que
// pede p50/p95/p99 POR cenário/endpoint.
const registerDuration = new Trend("register_duration", true);
const loginDuration = new Trend("login_duration", true);
const meDuration = new Trend("me_duration", true);
const rateLimited429 = new Counter("rate_limited_429");

const RUN_MODE = __ENV.RUN_MODE || "capacity";
const STAGE = __ENV.STAGE || "baseline"; // baseline|stress|spike

const STAGE_PROFILES = {
  // Progressão de concorrência pedida na seção 12 (10/25/50/100 — 250/500 não fazem sentido
  // neste hardware/single-host de desenvolvimento; ver docs/performance.md "Limitações").
  baseline: [
    { duration: "20s", target: 10 },
    { duration: "20s", target: 25 },
    { duration: "20s", target: 50 },
    { duration: "20s", target: 100 },
  ],
  // Stress test (seção 14): mesma progressão, sustentada por mais tempo em cada patamar, para
  // dar tempo do sistema efetivamente degradar (ou não) em vez de só passar por ele.
  stress: [
    { duration: "30s", target: 25 },
    { duration: "60s", target: 50 },
    { duration: "60s", target: 100 },
    { duration: "60s", target: 150 },
  ],
  // Spike test (seção 15): baseline baixo, salto abrupto, volta ao baseline — observa
  // recuperação.
  spike: [
    { duration: "20s", target: 5 },
    { duration: "10s", target: 100 },
    { duration: "20s", target: 5 },
  ],
  // Modo de throttling: poucos VUs, tempo suficiente para ultrapassar 20/min facilmente.
  throttling: [{ duration: "90s", target: 5 }],
};

export const options = {
  stages: STAGE_PROFILES[RUN_MODE === "throttling" ? "throttling" : STAGE],
  thresholds: {
    // Sem threshold de erro rígido aqui — 429 é esperado no modo throttling; a taxa de erro
    // real (5xx/timeout) é avaliada manualmente no relatório, não como um pass/fail do k6.
  },
};

export default function () {
  const email = randomEmail("auth");

  const registerRes = http.post(
    `${API_BASE}/api/auth/register`,
    JSON.stringify({ email, password: TEST_PASSWORD }),
    { headers: { "Content-Type": "application/json" }, tags: { name: "register" } }
  );
  check(registerRes, {
    "register: 201 or 429": (r) => r.status === 201 || r.status === 429,
  });
  registerDuration.add(registerRes.timings.duration);
  if (registerRes.status === 429) rateLimited429.add(1);

  const loginRes = http.post(
    `${API_BASE}/api/auth/login`,
    JSON.stringify({ email, password: TEST_PASSWORD }),
    { headers: { "Content-Type": "application/json" }, tags: { name: "login" } }
  );
  check(loginRes, { "login: 200 or 429": (r) => r.status === 200 || r.status === 429 });
  loginDuration.add(loginRes.timings.duration);
  if (loginRes.status === 429) rateLimited429.add(1);

  if (loginRes.status === 200) {
    const token = loginRes.json("accessToken");
    const meRes = http.get(`${API_BASE}/api/auth/me`, {
      ...authHeaders(token),
      tags: { name: "me" },
    });
    check(meRes, { "me: 200 or 429": (r) => r.status === 200 || r.status === 429 });
    meDuration.add(meRes.timings.duration);
    if (meRes.status === 429) rateLimited429.add(1);
  }

  sleep(1);
}
