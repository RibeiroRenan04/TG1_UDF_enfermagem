-- =============================================================================
--  Migration 006 – Horário de Brasília (GMT-3) nos registros de ponto
--
--  Os registros passaram a ser gravados no horário oficial do estágio
--  (América/São_Paulo, GMT-3 sem horário de verão) em vez de UTC. As linhas
--  já existentes foram gravadas em UTC e aparecem 3 horas adiantadas nas telas —
--  este script converte esses valores para o horário local.
--
--  ATENÇÃO: execute este script UMA ÚNICA VEZ, e somente junto com o deploy do
--  backend que passa a gravar em GMT-3. Rodar duas vezes subtrai 6 horas.
--  A tabela de controle "MigracoesAplicadas" abaixo garante isso.
-- =============================================================================

BEGIN;

-- ─────────────────────────────────────────────────────────────────────────────
--  0) Controle de execução única (a conversão de fuso não é idempotente)
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "MigracoesAplicadas" (
    "Nome"       TEXT        PRIMARY KEY,
    "AplicadaEm" TIMESTAMP   NOT NULL DEFAULT NOW()
);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM "MigracoesAplicadas" WHERE "Nome" = '006_fuso_brasilia') THEN
        RAISE EXCEPTION
            'A migração 006 já foi aplicada neste banco. Executá-la de novo deslocaria os horários mais 3 horas.';
    END IF;
END $$;

-- ─────────────────────────────────────────────────────────────────────────────
--  1) Registros de ponto: UTC → Brasília
-- ─────────────────────────────────────────────────────────────────────────────
UPDATE "RegistrosPresenca"
SET "RegistradoEm" = "RegistradoEm" - INTERVAL '3 hours',
    "ValidadoEm"   = "ValidadoEm"   - INTERVAL '3 hours',
    "CriadoEm"     = "CriadoEm"     - INTERVAL '3 hours';

-- ─────────────────────────────────────────────────────────────────────────────
--  2) Irregularidades: mesmos carimbos, para as datas baterem com as do ponto
-- ─────────────────────────────────────────────────────────────────────────────
UPDATE "Irregularidades"
SET "CriadoEm"            = "CriadoEm"            - INTERVAL '3 hours',
    "AtualizadoEm"        = "AtualizadoEm"        - INTERVAL '3 hours',
    "CienciaPreceptorEm"  = "CienciaPreceptorEm"  - INTERVAL '3 hours',
    "DecididoProfessorEm" = "DecididoProfessorEm" - INTERVAL '3 hours';

-- ─────────────────────────────────────────────────────────────────────────────
--  3) Alinha "EscalasRodizio" ao modelo do backend
--
--     "RotuloPeriodo" aceitava NULL no banco, mas o backend mapeia a propriedade
--     como texto obrigatório: qualquer rodízio gravado sem período derrubava a
--     listagem de escalas com "Column 'RotuloPeriodo' is null". Preenche os nulos
--     e passa a exigir o valor, como já acontece com turno e tipo de atividade.
-- ─────────────────────────────────────────────────────────────────────────────
UPDATE "EscalasRodizio" SET "RotuloPeriodo" = '' WHERE "RotuloPeriodo" IS NULL;
UPDATE "EscalasRodizio" SET "TipoAtividade" = 'assistencia' WHERE "TipoAtividade" IS NULL;
UPDATE "EscalasRodizio" SET "Turno" = 'manha' WHERE "Turno" IS NULL;

ALTER TABLE "EscalasRodizio"
    ALTER COLUMN "RotuloPeriodo" SET DEFAULT '',
    ALTER COLUMN "RotuloPeriodo" SET NOT NULL,
    ALTER COLUMN "TipoAtividade" SET DEFAULT 'assistencia',
    ALTER COLUMN "TipoAtividade" SET NOT NULL,
    ALTER COLUMN "Turno" SET NOT NULL;


-- ─────────────────────────────────────────────────────────────────────────────
--  4) Marca a migração como aplicada
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO "MigracoesAplicadas" ("Nome") VALUES ('006_fuso_brasilia');

-- ─────────────────────────────────────────────────────────────────────────────
--  5) Conferência: o horário mais recente deve bater com o relógio de Brasília
-- ─────────────────────────────────────────────────────────────────────────────
SELECT NOW() AT TIME ZONE 'America/Sao_Paulo' AS "AgoraEmBrasilia",
       MAX("RegistradoEm")                    AS "UltimoPontoRegistrado"
FROM   "RegistrosPresenca";

SELECT to_char("RegistradoEm", 'DD/MM/YYYY HH24:MI') AS "Registro",
       "Tipo", "Status"
FROM   "RegistrosPresenca"
ORDER  BY "RegistradoEm" DESC
LIMIT  5;

COMMIT;
