-- =============================================================================
--  EstágioCheck UDF – Migration 001: UDF Features
--  Banco: PostgreSQL 14+
--  Execução: psql -U <user> -d <database> -f 001_udf_features.sql
-- =============================================================================

-- ── 1. Campos adicionais na tabela users ──────────────────────────────────────

ALTER TABLE users
  ADD COLUMN IF NOT EXISTS rgm                VARCHAR(30)  UNIQUE,
  ADD COLUMN IF NOT EXISTS semester           SMALLINT     CHECK (semester IN (7, 8)),
  ADD COLUMN IF NOT EXISTS shift              VARCHAR(10)  CHECK (shift IN ('manha', 'tarde', 'noite')),
  ADD COLUMN IF NOT EXISTS must_change_password BOOLEAN    NOT NULL DEFAULT FALSE,
  ADD COLUMN IF NOT EXISTS must_set_email     BOOLEAN      NOT NULL DEFAULT FALSE,
  ADD COLUMN IF NOT EXISTS is_active          BOOLEAN      NOT NULL DEFAULT TRUE;

-- Índice para busca rápida por RGM (login dos alunos)
CREATE UNIQUE INDEX IF NOT EXISTS idx_users_rgm ON users (rgm) WHERE rgm IS NOT NULL;

-- ── 2. Histórico de semestres dos alunos ────────────────────────────────────
--  Armazena semestres anteriores para computar carga horária acumulada
CREATE TABLE IF NOT EXISTS student_semester_history (
  id           UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id      UUID         NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  semester     SMALLINT     NOT NULL CHECK (semester IN (7, 8)),
  shift        VARCHAR(10)  NOT NULL CHECK (shift IN ('manha', 'tarde', 'noite')),
  start_date   DATE         NOT NULL,
  end_date     DATE,
  total_hours  NUMERIC(8,2) NOT NULL DEFAULT 0,
  created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_semester_history_user ON student_semester_history (user_id);

-- ── 3. Códigos de recuperação de senha ─────────────────────────────────────
CREATE TABLE IF NOT EXISTS password_reset_codes (
  id         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
  email      VARCHAR(255) NOT NULL,
  code       CHAR(6)     NOT NULL,
  expires_at TIMESTAMPTZ NOT NULL,
  used       BOOLEAN     NOT NULL DEFAULT FALSE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_reset_codes_email ON password_reset_codes (email);

-- Limpa códigos expirados automaticamente (requer pg_cron ou chamada periódica da API)
-- DELETE FROM password_reset_codes WHERE expires_at < NOW();

-- =============================================================================
--  BACKEND – Novos endpoints necessários
-- =============================================================================
--
--  POST /api/auth/first-access
--    Body:  { email: string, newPassword: string }
--    Ação:  Atualiza email + senha, define must_change_password=false, must_set_email=false
--    Auth:  Bearer token (aluno autenticado com flag ativa)
--
--  POST /api/auth/forgot-password
--    Body:  { email: string }        -- deve terminar com @cs.udf.edu.br
--    Ação:  Gera código aleatório de 6 dígitos, salva em password_reset_codes
--           com expires_at = NOW() + interval '15 minutes', envia por e-mail
--    Auth:  Público
--
--  POST /api/auth/verify-reset-code
--    Body:  { email: string, code: string }
--    Ação:  Valida código (não expirado, não usado)
--    Auth:  Público
--
--  POST /api/auth/reset-password
--    Body:  { email: string, code: string, newPassword: string }
--    Ação:  Valida código, atualiza senha, marca código como usado
--    Auth:  Público
--
--  POST /api/users/staff
--    Body:  { fullName, email, password, role, institution?, phone? }
--    Ação:  Cria usuário preceptor ou supervisor; must_change_password=true
--    Auth:  supervisor
--
--  POST /api/users/bulk-import
--    Body:  { students: [{ rgm, fullName, semester, shift }] }
--    Ação:  Para cada aluno:
--             – Se RGM já existe → atualiza semester/shift (conta como updated)
--             – Se não existe    → cria usuário com:
--                 email=null, password=hash(rgm), role=aluno,
--                 must_change_password=true, must_set_email=true
--    Retorno: { imported: N, updated: M, errors: [...] }
--    Auth:  supervisor
--
--  POST /api/users/advance-semester
--    Ação:
--      1. Busca todos alunos com semester=8 e is_active=true
--         → Para cada um: insere em student_semester_history (semestre 8 + total_hours)
--                         define is_active=false (formado)
--      2. Busca todos alunos com semester=7 e is_active=true
--         → Para cada um: insere em student_semester_history (semestre 7 + total_hours)
--                         atualiza semester=8
--    Retorno: { advanced: N, graduated: M }
--    Auth:  supervisor
--
-- =============================================================================
--  ENVIO DE E-MAIL (forgot-password)
-- =============================================================================
--  Utilizar biblioteca nodemailer (Node.js) ou equivalente.
--  Template sugerido:
--    Assunto: "EstágioCheck UDF – Código de recuperação de senha"
--    Corpo:
--      "Olá! Seu código de verificação é: <CODIGO>
--       O código expira em 15 minutos. Se não foi você, ignore este e-mail."
--
-- =============================================================================
