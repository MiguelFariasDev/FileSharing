# Deployment

Este documento cobre apenas o que a Etapa 6 (Hangfire + expiração/limpeza automática) introduz com relevância para execução/deploy. AWS ECS Fargate, RDS de produção e CI/CD (`.github/workflows/`) são etapas futuras — nada disso é implementado ou documentado aqui ainda.

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

## Fora de escopo desta etapa

AWS ECS Fargate, AWS RDS de produção, AWS Secrets Manager, Application Load Balancer, GitHub Actions (`api-web-ci.yml`/`mobile-android-ci.yml`) — nenhum desses foi implementado ou alterado pela Etapa 6. Este documento será expandido quando essas etapas forem implementadas.
