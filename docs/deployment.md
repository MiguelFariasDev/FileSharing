# Deployment

Este documento cobre apenas o que as Etapas 6 (Hangfire + expiração/limpeza automática) e 7 (SignalR + notificação em tempo real) introduzem com relevância para execução/deploy. AWS ECS Fargate, RDS de produção e CI/CD (`.github/workflows/`) são etapas futuras — nada disso é implementado ou documentado aqui ainda.

## Hangfire em produção

- **Storage:** PostgreSQL — a mesma instância/base já usada por `ApplicationDbContext` (`ConnectionStrings:Postgres`), via `Hangfire.PostgreSql`. Nenhuma infraestrutura adicional (Redis, segunda base) é necessária. Ver `docs/database.md`.
- **Server:** `services.AddHangfireServer()` registra o processamento de jobs **no mesmo processo** da API (não um worker separado) — cada instância da API que subir também roda um `HangfireServer`. Múltiplas instâncias rodando simultaneamente é seguro: `[DisableConcurrentExecution]` (no método da interface `IExpiredFileCleanupJob`) usa o lock distribuído do próprio Hangfire, apoiado no PostgreSQL compartilhado, para garantir que só uma execução do job `expired-file-cleanup` rode por vez em todo o cluster.
- **Dashboard:** não exposto nesta etapa. `app.UseHangfireDashboard()` nunca é chamado — `/hangfire` não é uma rota válida. Se uma etapa futura precisar do dashboard, ele deve ser adicionado atrás de autenticação/autorização (nunca anônimo), nunca simplesmente reativado sem esse filtro.
- **Habilitar/desabilitar:** `ExpirationCleanup:Enabled` (padrão `true`). Quando `false`, ou quando `ConnectionStrings:Postgres` não está configurado, a aplicação sobe normalmente sem registrar storage/server/job do Hangfire — `IExpiredFileCleanupJob` continua resolvível via DI (para uso direto/testes), apenas nunca agendado.

## Configuração obrigatória

Igual ao padrão já estabelecido para `Jwt:SecretKey` e as credenciais AWS (ver `docs/security.md`): nenhum segredo novo foi introduzido por esta etapa — o Hangfire reaproveita a connection string `Postgres` já exigida desde etapas anteriores. Variáveis relevantes (nenhuma é um segredo por si só):

```json
"ExpirationCleanup": {
  "Enabled": true,
  "IntervalMinutes": 15,
  "BatchSize": 100
}
```

- `IntervalMinutes`: usado para montar a expressão cron (`*/{IntervalMinutes} * * * *`) do job recorrente `expired-file-cleanup`. Aproximado, não depende de precisão de segundo.
- `BatchSize`: máximo de arquivos expirados processados por execução — limita memória e duração de cada execução; um backlog maior drena ao longo de execuções subsequentes.

## O que verificar antes de subir em um ambiente novo

1. `ConnectionStrings:Postgres` aponta para um PostgreSQL alcançável a partir do processo da API (o mesmo já exigido para `ApplicationDbContext`).
2. O usuário do banco tem permissão para criar o schema `hangfire` na primeira subida (o mesmo usuário já usado pelo EF Core é suficiente em desenvolvimento; em produção, confirmar que a role tem `CREATE SCHEMA`).
3. `ExpirationCleanup:Enabled` está `true` (ou ausente — o padrão já é `true`) nos ambientes onde a limpeza automática deve rodar.
4. Nenhuma rota `/hangfire` foi adicionada manualmente sem um filtro de autorização.
5. Nenhuma rota `/hubs/notifications` foi exposta sem `[Authorize]` no Hub.

## SignalR em produção (Etapa 7)

- **Sem backplane nesta etapa.** O SignalR usa seu armazenamento de conexões em memória padrão — sem Redis, sem Azure SignalR Service, conforme pedido explicitamente para esta etapa.
- **Funciona corretamente com uma única instância da Api.** Toda a garantia de isolamento por usuário (`Clients.User`) e suporte a múltiplas conexões do mesmo usuário (várias abas/dispositivos) funciona sem nenhuma configuração adicional enquanto só uma instância do processo estiver rodando.
- **Limitação conhecida para múltiplas instâncias.** Se uma implantação futura rodar mais de uma instância da Api simultaneamente atrás de um load balancer (por exemplo, várias tasks no AWS ECS Fargate mencionado no `CLAUDE.md`), uma conexão SignalR estabelecida com a instância A não é visível pela instância B — um download processado pela instância B não conseguiria notificar em tempo real um dono cuja conexão de navegador está aberta com a instância A (a notificação simplesmente não chegaria; nada quebra, nada vaza para o usuário errado). Isso é puramente uma limitação de alcance/entrega, não de segurança: o isolamento por usuário continua correto independentemente da topologia.
- **Solução para múltiplas instâncias: um backplane.** Redis (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) ou o Azure SignalR Service gerenciado são as opções padrão do ecossistema ASP.NET Core para sincronizar mensagens entre instâncias. **Não implementado nesta etapa** — é uma decisão de infraestrutura de deployment/escala a ser tomada quando (e se) a Api passar a rodar com mais de uma instância simultânea, não algo que precisa existir para o funcionamento correto hoje.
- **Sticky sessions não são necessárias com um backplane**, mas **são necessárias sem um** se o load balancer não garantir afinidade de conexão — sem backplane e sem sticky sessions, uma reconexão do cliente poderia cair em uma instância diferente da que originalmente aceitou a conexão, o que já é tratado pelo protocolo de negociação do SignalR (uma nova conexão é sempre válida), mas o cliente perderia qualquer estado em memória associado à conexão anterior. Sem estado em memória por conexão nesta implementação (o Hub não guarda nada), o impacto prático disso hoje é nulo.

## Fora de escopo destas etapas

AWS ECS Fargate, AWS RDS de produção, AWS Secrets Manager, Application Load Balancer, GitHub Actions (`api-web-ci.yml`/`mobile-android-ci.yml`), backplane Redis/Azure SignalR — nenhum desses foi implementado ou alterado pelas Etapas 6/7. Este documento será expandido quando essas etapas forem implementadas.
