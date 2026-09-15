// Cenário "SignalR" (Etapa 16, seção 17) — escopo deliberadamente reduzido: mede o custo de
// abrir N conexões autenticadas simultâneas ao Hub (negotiate HTTP + handshake WebSocket JSON) e
// mantê-las abertas por um curto período — não um benchmark de latência de entrega de mensagem
// (`FileDownloaded`), que já é coberto pelos testes funcionais reais e determinísticos
// (NotificationHubTests, FileSharing.ApiTests) contra duas conexões reais simultâneas — abrir
// centenas de conexões apenas para medir o tempo de handshake não testaria nada que o transporte
// HTTP/WebSocket do Kestrel já não garanta.
//
// Nunca broadcast — cada VU só abre a própria conexão (Clients.User(userId) continua a única
// forma de entrega, inalterada); isto só mede custo de conexão/handshake, nunca dispara nem
// depende de nenhuma notificação real sendo entregue a outra conexão.
import http from "k6/http";
import ws from "k6/ws";
import { check } from "k6";
import { Trend } from "k6/metrics";
import { API_BASE, registerAndLogin } from "./lib/helpers.js";

const CONNECTIONS = Number(__ENV.CONNECTIONS || 10);
const HOLD_SECONDS = Number(__ENV.HOLD_SECONDS || 10);

const negotiateDuration = new Trend("negotiate_duration", true);
const handshakeDuration = new Trend("ws_handshake_duration", true);

export const options = { vus: CONNECTIONS, iterations: CONNECTIONS };

export default function () {
  const { token } = registerAndLogin("signalr");
  if (!token) return;

  const negotiateStart = Date.now();
  const negotiateRes = http.post(
    `${API_BASE}/hubs/notifications/negotiate?negotiateVersion=1`,
    null,
    { headers: { Authorization: `Bearer ${token}` }, tags: { name: "negotiate" } }
  );
  negotiateDuration.add(Date.now() - negotiateStart);
  check(negotiateRes, { "negotiate: 200": (r) => r.status === 200 });
  if (negotiateRes.status !== 200) return;

  const { connectionToken } = negotiateRes.json();
  const wsUrl = `${API_BASE.replace("http", "ws")}/hubs/notifications?id=${connectionToken}&access_token=${token}`;

  const handshakeStart = Date.now();
  const res = ws.connect(wsUrl, {}, function (socket) {
    socket.on("open", () => {
      // Protocolo JSON do SignalR: mensagens terminadas em \x1e (record separator).
      socket.send(JSON.stringify({ protocol: "json", version: 1 }) + "\x1e");
    });

    socket.on("message", (data) => {
      if (data === "{}\x1e") {
        handshakeDuration.add(Date.now() - handshakeStart);
      }
    });

    socket.setTimeout(() => {
      socket.close();
    }, HOLD_SECONDS * 1000);
  });

  check(res, { "ws connect: status 101": (r) => r && r.status === 101 });
}
