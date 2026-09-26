# Continuidade do negócio e backup (ISO 22301)

## Objetivos

| Indicador | Meta | Como é atingida |
|---|---|---|
| **RPO** (perda máxima de dados) | 24 h | Backup lógico diário às 03:00 (Brasília) + backups do próprio Supabase |
| **RTO** (tempo para voltar ao ar) | 4 h em horário útil | Restauração por script em projeto Supabase novo + redeploy no Railway/Vercel |
| **Período crítico** | Seg–sáb, 06:00–23:00 | Horário dos turnos de estágio (registro de ponto) |

Impacto de indisponibilidade: o aluno não consegue registrar ponto. O registro retroativo é
feito pela coordenação (irregularidade/justificativa), então a perda é administrativa e não de
carga horária — isso justifica o RTO de 4 h.

## Camadas de backup

1. **Supabase (provedor):** backups diários automáticos no plano Pro (7 dias) e PITR opcional.
   No plano gratuito **não há backup** — por isso existe a camada 2.
2. **Backup independente (este repositório):** `.github/workflows/backup.yml` roda
   `database/backup.sh` todo dia, gera `pg_dump` no formato custom, **criptografa com AES-256
   (GPG)** e guarda como artefato do GitHub por 30 dias, com checksum SHA-256.
3. **Cópia fora do GitHub (recomendada):** mensalmente, baixar o artefato mais recente e guardar
   em armazenamento institucional da UDF (retenção de 12 meses).

### Configuração do backup automático

1. No Supabase, criar um usuário somente leitura:
   ```sql
   CREATE ROLE backup_leitura WITH LOGIN PASSWORD '<senha-forte>';
   GRANT USAGE ON SCHEMA public TO backup_leitura;
   GRANT SELECT ON ALL TABLES IN SCHEMA public TO backup_leitura;
   ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO backup_leitura;
   -- RLS está ligado em todas as tabelas: o usuário de backup precisa ignorá-lo.
   ALTER ROLE backup_leitura BYPASSRLS;
   ```
2. No GitHub (Settings → Secrets and variables → Actions):
   - `BACKUP_DATABASE_URL` = connection string com `backup_leitura`;
   - `BACKUP_PASSPHRASE` = frase longa aleatória. **Guardar também fora do GitHub** (cofre de
     senhas da coordenação): sem ela o backup é irrecuperável.
3. Rodar o workflow manualmente uma vez (Actions → Backup diário → Run workflow) e conferir o artefato.

### Backup manual

```bash
BACKUP_PASSPHRASE='…' ./database/backup.sh "postgresql://…" ./backups
```

## Restauração

```bash
BACKUP_PASSPHRASE='…' ./database/restore.sh backups/estagiocheck_AAAAMMDD_HHMMSS.dump.gpg "postgresql://…"
```

O script confere o SHA-256, descriptografa, pede confirmação digitando `RESTAURAR` e restaura com
`pg_restore --clean`. Depois conferir `SELECT "Nome" FROM "MigracoesAplicadas"` — deve listar até o
último script aplicado.

### Teste de restauração (trimestral)

1. Criar projeto Supabase descartável.
2. Restaurar o backup mais recente nele.
3. Apontar uma API local (`ConnectionStrings__DefaultConnection`) e fazer login com um usuário de teste.
4. Conferir contagens: `Usuarios`, `RegistrosPresenca`, `LogsAuditoria`.
5. Registrar data, backup usado, tempo gasto e resultado na tabela abaixo; apagar o projeto.

| Data | Backup | Tempo total | Resultado | Responsável |
|---|---|---|---|---|
| | | | | |

## Plano de recuperação por cenário

| Cenário | Ação | Responsável |
|---|---|---|
| API fora do ar (Railway) | Ver logs no Railway; redeploy do último commit estável da `main` (`/health` e `/health/ready`) | Desenvolvimento |
| Front fora do ar (Vercel) | Promover o deploy anterior no painel da Vercel (rollback instantâneo) | Desenvolvimento |
| Banco indisponível (Supabase) | Acompanhar status.supabase.com; se > RTO, criar projeto novo, restaurar backup, trocar `ConnectionStrings__DefaultConnection` no Railway | Desenvolvimento |
| Dados corrompidos por erro humano | Localizar a alteração na trilha (`GET /api/auditoria?entidadeId=…`), que traz os valores *de → para*; corrigir pontualmente ou restaurar tabela específica do backup (`pg_restore -t`) | Desenvolvimento + coordenação |
| Credencial vazada | Trocar a credencial (banco, `Jwt__Key` — invalida todas as sessões —, SMTP); revisar a trilha de auditoria; seguir resposta a incidentes | Desenvolvimento |
| Perda da conta do provedor | Restaurar a cópia do armazenamento institucional (camada 3) em novo provedor | Coordenação |

## Resposta a incidentes

Incidente de segurança = acesso indevido, vazamento, perda ou alteração não autorizada de dados.

1. **Conter** (até 1 h da detecção): revogar a credencial afetada, desativar a conta suspeita
   (o token perde validade em até 1 min), bloquear a origem se necessário.
2. **Avaliar** (até 24 h): usar `GET /api/auditoria` (por usuário, IP, período) e os logs do
   Railway (buscar pelo `X-Correlation-Id`) para determinar quais dados e quantos titulares.
3. **Comunicar** (LGPD art. 48 e Resolução CD/ANPD 15/2024): se houver risco ou dano relevante,
   o encarregado comunica a **ANPD e os titulares em até 3 dias úteis**.
4. **Corrigir e registrar**: causa raiz, correção, e lição aprendida neste repositório.
