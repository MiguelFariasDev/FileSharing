// Cria N arquivos reais (upload completo via S3/LocalStack, nunca um atalho) para o benchmark do
// Hangfire (Etapa 16, seções 18/19) — o SQL de infrastructure/docker (ou psql direto) depois
// atualiza ExpiresAt desses arquivos para o passado, já que a API nunca permite isso via HTTP
// (ExpiresAt é sempre UploadedAtUtc+24h, regra de domínio preservada).
// Uso: k6 run -e FILE_COUNT=100 load-tests/k6/seed-files-for-cleanup.js
import { registerAndLogin, createAndCompleteFile } from "./lib/helpers.js";

const FILE_COUNT = Number(__ENV.FILE_COUNT || 100);

export const options = { vus: 1, iterations: 1 };

export default function () {
  const { token, email } = registerAndLogin("cleanupseed");
  console.log(`seed user: ${email}`);
  for (let i = 0; i < FILE_COUNT; i++) {
    createAndCompleteFile(token, 1024, "cleanupseed");
  }
}
