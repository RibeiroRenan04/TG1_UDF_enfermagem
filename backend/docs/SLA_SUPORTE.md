# SLA, suporte e manutenção

Proposta de acordo de nível de serviço do EstágioCheck. Os números são metas internas; a UDF
deve ajustá-los à equipe de suporte disponível antes de formalizar.

## Disponibilidade

| Item | Meta |
|---|---|
| Disponibilidade mensal da API e do site | **99,5%** (≈ 3 h 40 min de indisponibilidade/mês) no período crítico |
| Período crítico | Seg–sáb, 06:00–23:00 (Brasília) — horários de turno de estágio |
| Medição | Monitor externo a cada 5 min em `GET /health/ready` (verifica banco, latência e versão) |
| Fora da meta | Manutenções programadas e indisponibilidade dos provedores (Supabase, Railway, Vercel) além do controle da equipe |

Dependências e seus próprios SLAs: Supabase, Railway e Vercel publicam status em
status.supabase.com, status.railway.app e vercel-status.com.

## Severidade e prazos de atendimento

| Severidade | Exemplo | Primeira resposta | Solução ou contorno |
|---|---|---|---|
| **S1 — Crítica** | Sistema fora do ar; ninguém registra ponto; suspeita de vazamento de dados | 1 h útil | 4 h úteis |
| **S2 — Alta** | Função importante falha para um grupo (ex.: check-in de uma unidade, emissão de certificado) | 4 h úteis | 1 dia útil |
| **S3 — Média** | Erro com contorno (ex.: relatório com filtro errado) | 1 dia útil | 5 dias úteis |
| **S4 — Baixa** | Dúvida, ajuste visual, sugestão de melhoria | 2 dias úteis | Próxima versão planejada |

Horário útil: seg–sex, 08:00–18:00. Incidentes S1 de segurança seguem também o procedimento de
[resposta a incidentes](CONTINUIDADE_BACKUP.md#resposta-a-incidentes).

## Canais de suporte

1. **Aluno** → preceptor ou secretaria do estágio (1º nível: senha, dúvidas de uso).
2. **Secretaria/professor** → equipe de desenvolvimento (2º nível), informando:
   - o que tentou fazer, usuário afetado, data/hora;
   - o **código de rastreamento** (`X-Correlation-Id`, também devolvido como `traceId` nos erros
     internos) — com ele o log exato é localizado no Railway e na trilha de auditoria.
3. Pedidos de titulares sobre dados pessoais (LGPD) → encarregado de proteção de dados.

Desbloqueios comuns do 1º nível:
- **Senha esquecida:** "Esqueci minha senha" (código por e-mail, válido por 15 min) ou, para aluno,
  o professor redefine para o RGM em Usuários.
- **Conta bloqueada por tentativas:** aguarda 15 min ou redefine a senha (desbloqueia na hora).
- **Conta inativa:** o professor reativa o usuário.

## Manutenção

| Tipo | Quando | Aviso prévio |
|---|---|---|
| Corretiva (bugs S1/S2) | A qualquer momento | Não exige; comunicar após |
| Preventiva/evolutiva | Janela: seg–sex após 23:00 ou domingo | 48 h, pelos canais da coordenação |
| Atualização de dependências | Semanal (PRs do Dependabot), junto das janelas | — |
| Scripts de banco (`database/0NN_*.sql`) | Na janela, **antes** do deploy do backend que depende deles | 48 h |

Todo deploy passa pelo CI (build, testes, dependências vulneráveis, segredos) e pela homologação
(Staging) antes da produção. Rollback: redeploy da versão anterior no Railway/Vercel.

## Versionamento e registro de mudanças

- Cada merge na `main` corresponde a uma versão implantável; a versão em execução aparece em
  `GET /health/ready` (`versao`, com o hash do commit).
- Mudanças que exigem ação (script de banco, variável de ambiente nova, novo login) são descritas
  no PR e no `database/README.md`.

## Indicadores acompanhados mensalmente

- Disponibilidade medida × meta.
- Chamados por severidade e percentual dentro do prazo.
- Falhas de login e bloqueios (`GET /api/auditoria?acao=login_bloqueado`) — pico indica ataque.
- Resultado do último teste de restauração de backup.
