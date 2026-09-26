# Conformidade — LGPD, ISO/IEC 27001, 27701, 25010 e 22301

Avaliação do EstágioCheck (API .NET 8 + Angular + PostgreSQL/Supabase) frente às normas e
requisitos pedidos, feita em 25/09/2026 na branch `feat/ajustes-iso`.

> **Importante:** normas ISO certificam **organizações e processos**, não só software. O que
> o código pode fazer está implementado abaixo; o que depende da UDF (políticas, nomeação do
> encarregado, contratos, análise de riscos) está listado em [Pendências organizacionais](#pendências-organizacionais).

Legenda: ✅ atende · 🟡 atende parcialmente · ❌ não atende · 🆕 implementado nesta branch

---

## 1. LGPD e Privacidade

| Requisito | Antes | Agora | Onde |
|---|---|---|---|
| Aviso de privacidade (art. 9º) | ❌ | 🆕 ✅ | `GET /api/privacidade/aviso` (`Services/Privacidade/AvisoPrivacidade.cs`) |
| Acesso e portabilidade (art. 18, II e V) | ❌ | 🆕 ✅ | `GET /api/privacidade/meus-dados` (titular) e `GET /api/privacidade/usuarios/{id}/dados` (professor) — JSON estruturado |
| Anonimização (art. 18, IV / art. 16) | ❌ | 🆕 ✅ | `POST /api/privacidade/usuarios/{id}/anonimizar` — só conta inativa; remove nome, e-mail, RGM, telefone, localização e foto do ponto, IP de assinatura; preserva horas/avaliações |
| Minimização em logs (art. 6º, III) | ❌ e-mail completo no log | 🆕 ✅ | `Mascara.Email`; trilha de auditoria mascara nome, e-mail, RGM, telefone, coordenadas e IPs |
| Retenção e descarte (art. 15/16) | ❌ códigos de senha nunca eram apagados | 🆕 ✅ | `RetencaoDadosService` (diário): códigos vencidos e auditoria > `Auditoria:RetencaoDias` |
| Registro das operações (art. 37) | ❌ | 🆕 ✅ | Trilha de auditoria (seção 5) |
| Segurança do tratamento (art. 46) | 🟡 | 🆕 ✅ | Seção 2 |
| Termo de responsabilidade de acesso | ✅ | ✅ | `GET /api/auth/terms` (perfis não-aluno) |
| Transferência internacional (art. 33) | ❌ não documentada | 🟡 | Supabase em `us-west` (EUA): informado no aviso; **falta** cláusula contratual — ver pendências |
| Comunicação de incidente (art. 48) | ❌ | 🟡 | Procedimento em [CONTINUIDADE_BACKUP.md](CONTINUIDADE_BACKUP.md#resposta-a-incidentes); execução é da UDF |

**Base legal adotada** (validar com o jurídico): obrigação legal — Lei do Estágio 11.788/2008 —
para frequência e carga horária; execução de contrato educacional para avaliação e certificado;
legítimo interesse para geolocalização antifraude e logs de segurança. Não se usa consentimento
como base, porque o aluno não pode recusar o controle de frequência.

## 2. ISO/IEC 27001 — Segurança da informação (controles do Anexo A)

| Controle | Situação | Detalhe |
|---|---|---|
| A.5.15/5.18 Controle e revogação de acesso | 🆕 ✅ | Conta inativa não faz login (403) e o **token já emitido deixa de valer em até 1 min** (`ValidacaoUsuarioAtivo`). Perfis por `[Authorize(Roles)]` em todas as rotas |
| A.5.17 Informação de autenticação | 🆕 ✅ | Política de senha: ≥ 8, letras e números, não pode conter RGM nem o login (`PoliticaSenha`). BCrypt no armazenamento |
| A.8.5 Autenticação segura | 🆕 ✅ | Bloqueio de 15 min após 5 erros por conta (login e código de recuperação); rate limit de 60 req/min por IP nas rotas `/api/auth`; tempo de resposta igual para e-mail inexistente; código OTP com gerador criptográfico e só o último código vale |
| A.8.9 Gestão de configuração / segredos | ❌ → 🆕 🟡 | **`appsettings.Development.json` com senha do banco e chave JWT estava versionado e publicado no GitHub.** Removido do versionamento (fica só local) e substituído por `appsettings.Development.example.json`; CI com gitleaks impede nova ocorrência. **A senha do banco dev e a chave JWT precisam ser trocadas** — continuam no histórico do git |
| A.8.12 Prevenção de vazamento | 🆕 ✅ | JWT exige chave ≥ 32 bytes (a API não sobe sem isso); Swagger desligado em produção (`Swagger:Habilitado`) |
| A.8.15/8.16 Registro e monitoramento | 🆕 ✅ | Trilha de auditoria + `X-Correlation-Id` em toda resposta e nos logs; `traceId` no corpo de erro 500 |
| A.8.20–8.23 Segurança de redes/web | 🆕 ✅ | HSTS (fora de dev), CSP `default-src 'none'`, `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy`, `Cache-Control: no-store`; CORS com origens explícitas; cabeçalhos de proxy tratados (IP real do cliente) |
| A.8.24 Criptografia | ✅ | TLS em trânsito (Railway/Vercel/Supabase), BCrypt para senhas, backup cifrado com AES-256 (GPG). Recomendado: trocar `Trust Server Certificate=true` por `SSL Mode=VerifyFull` na connection string |
| A.8.8 Vulnerabilidades técnicas | 🆕 ✅ | Dependabot (NuGet + Actions) e verificação de pacotes vulneráveis no CI |
| A.8.25–8.29 Desenvolvimento seguro e testes | 🆕 ✅ | Pipeline `ci.yml`: build + 249 testes + pacotes vulneráveis + varredura de segredos em todo PR |
| A.8.13 Backup | 🆕 🟡 | Ver seção 6 |
| Row Level Security no Supabase | ✅ | Script 010 (e 015 para a tabela nova): a API REST pública do Supabase não lê nada |

## 3. ISO/IEC 27701 — Gestão de informações de privacidade

A 27701 estende a 27001 com controles de controlador (Anexo A) e operador (Anexo B). No
software: finalidade e base legal documentadas (aviso), minimização (máscaras), direitos do
titular (exportação/anonimização), retenção (rotina diária), registro de tratamento (auditoria)
e privacidade por padrão (localização só no momento do ponto; Permissions-Policy bloqueia
câmera/microfone/geolocalização nas respostas da API). O que falta é organizacional: inventário
de dados (ROPA), RIPD/DPIA da geolocalização e contratos com operadores.

## 4. ISO/IEC 25010 — Qualidade do produto de software

| Característica | Situação | Evidência |
|---|---|---|
| Adequação funcional | ✅ | 249 testes automatizados cobrindo regras de ponto, rodízios, alocações, certificados, irregularidades, segurança e LGPD |
| Eficiência de desempenho | ✅ | Consultas `AsNoTracking`, índices nas tabelas de maior volume, geocodificação em fila de segundo plano |
| Compatibilidade | ✅ | API REST/JSON documentada por OpenAPI (Swagger em dev/homologação) |
| Usabilidade | ✅ | Erros sempre `{ message, field, code, errors }` em português (`ErrosApi`) |
| Confiabilidade | 🆕 ✅ | `/health` (liveness) e `/health/ready` (banco, latência, versão); tratador global de exceções; auditoria na mesma transação do dado |
| Segurança | 🆕 ✅ | Seção 2 |
| Manutenibilidade | 🆕 ✅ | CI obrigatório, Dependabot, código organizado por camadas (Controllers/Services/Models/DTOs) |
| Portabilidade | ✅ | .NET 8 multiplataforma; banco provisionável por script (`database/apply_all.sh`) |

## 5. Auditoria e rastreabilidade

- **Automática:** `AuditoriaInterceptor` registra toda criação, alteração (só campos que mudaram,
  com *de → para*) e exclusão feita pela API, **na mesma transação** — não existe dado gravado sem
  trilha. Cache de geocodificação e códigos de senha ficam de fora.
- **Eventos:** login (sucesso, falha, bloqueio, conta inativa), primeiro acesso, aceite de termo,
  pedido/uso de código de recuperação, redefinição de senha, exportação e anonimização LGPD.
- **Cada registro:** data/hora (Brasília), usuário, perfil, ação, tabela, id, detalhes (JSON), IP,
  navegador e `IdCorrelacao` (o mesmo `X-Correlation-Id` da resposta HTTP e dos logs).
- **Imutável:** trigger no banco recusa `UPDATE` em `LogsAuditoria`; não há rota de alteração ou
  exclusão. Sem FK para `Usuarios`: a trilha sobrevive à anonimização.
- **Consulta:** `GET /api/auditoria` (somente professor) com filtros `de`, `ate`, `usuarioId`,
  `acao`, `entidade`, `entidadeId`, `sucesso` e paginação.
- **Retenção:** 5 anos (`Auditoria:RetencaoDias`), acima dos 6 meses do Marco Civil, art. 15.

## 6. Backup e continuidade — ISO 22301

Ver [CONTINUIDADE_BACKUP.md](CONTINUIDADE_BACKUP.md): RPO/RTO, backup diário cifrado
independente do Supabase (`.github/workflows/backup.yml`), scripts `database/backup.sh` e
`restore.sh`, teste trimestral de restauração, plano de recuperação por cenário e resposta a
incidentes.

## 7. SLA, suporte e manutenção

Ver [SLA_SUPORTE.md](SLA_SUPORTE.md): disponibilidade-alvo, severidades e prazos, canais,
janelas de manutenção, versionamento e monitoramento.

## 8. Arquitetura, infraestrutura e requisitos de segurança

```
Navegador ──HTTPS/HSTS──▶ Vercel (Angular SPA)
    │
    └──HTTPS + JWT (8h)──▶ Railway: API .NET 8
                              │  ForwardedHeaders → CorrelacaoMiddleware → ExceptionHandler
                              │  → HSTS/CabecalhosSeguranca → CORS → RateLimiter
                              │  → JWT (+ usuário ativo) → [Authorize(Roles)] → Controllers
                              │  → EF Core + AuditoriaInterceptor
                              ▼
                        Supabase PostgreSQL (TLS, RLS ligado, sem acesso pela API REST)
                              ▲
              GitHub Actions: backup diário cifrado (usuário somente leitura)
```

Requisitos de segurança de implantação (checklist de deploy):

- [ ] `ASPNETCORE_ENVIRONMENT=Production` no Railway (desliga Swagger e liga HSTS).
- [ ] `Jwt__Key` com ≥ 32 bytes aleatórios, **nova** (a antiga vazou pelo git) e diferente por ambiente.
- [ ] Senhas do banco de dev e de Staging trocadas no Supabase.
- [ ] Connection string por variável de ambiente, com `SSL Mode=VerifyFull` quando possível.
- [ ] Script `database/015_auditoria.sql` aplicado **antes** do deploy desta versão.
- [ ] `Privacidade__Encarregado__Email` preenchido com o e-mail do encarregado.
- [ ] Segredos `BACKUP_DATABASE_URL` (usuário somente leitura) e `BACKUP_PASSPHRASE` no GitHub.
- [ ] Monitor externo (ex.: UptimeRobot) em `/health/ready`.

---

## Pendências organizacionais

Não se resolvem com código — cabem à UDF/coordenação:

1. **Nomear o encarregado (DPO)** e publicar o contato (LGPD art. 41).
2. **Validar juridicamente** o aviso de privacidade e as bases legais.
3. **Contratos/DPAs com operadores** (Supabase, Railway, Vercel, provedor de e-mail) com
   cláusulas de transferência internacional (art. 33) — ou migrar o banco para região no Brasil
   (`sa-east-1`).
4. **RIPD/DPIA** da coleta de geolocalização (art. 38).
5. **Inventário de dados / ROPA** e política de retenção de documentos acadêmicos (prazo exato).
6. **Política de segurança da informação**, análise de riscos e declaração de aplicabilidade —
   base de um SGSI ISO 27001.
7. **Reescrever o histórico do git** (ex.: `git filter-repo`) se o repositório for público —
   trocar as credenciais é obrigatório de qualquer forma.
8. **Front-end:** exibir o aviso de privacidade, a opção "Meus dados" (download do JSON) e o
   `X-Correlation-Id` nas mensagens de erro; tratar 403 `conta_inativa` e 429 no login.
