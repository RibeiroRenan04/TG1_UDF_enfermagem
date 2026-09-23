-- 006 – converte ponto e irregularidades de UTC para Brasília (GMT-3). A conversão não é
-- idempotente (rodar duas vezes subtrairia 6 h): só roda se o 006 não consta em "MigracoesAplicadas".

BEGIN;

-- 0) Controle de execução única (a conversão de fuso não é idempotente)
CREATE TABLE IF NOT EXISTS "MigracoesAplicadas" (
    "Nome"       TEXT        PRIMARY KEY,
    "AplicadaEm" TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo')
);

DO $migracao$
BEGIN
    IF EXISTS (SELECT 1 FROM "MigracoesAplicadas" WHERE "Nome" = '006_fuso_brasilia') THEN
        RAISE NOTICE '006: conversão de fuso já aplicada neste banco — pulando os passos 1 e 2.';
        RETURN;
    END IF;

    -- 1) Registros de ponto: UTC → Brasília
    UPDATE "RegistrosPresenca"
    SET "RegistradoEm" = "RegistradoEm" - INTERVAL '3 hours',
        "ValidadoEm"   = "ValidadoEm"   - INTERVAL '3 hours',
        "CriadoEm"     = "CriadoEm"     - INTERVAL '3 hours';

    -- 2) Irregularidades: mesmos carimbos, para as datas baterem com as do ponto
    UPDATE "Irregularidades"
    SET "CriadoEm"            = "CriadoEm"            - INTERVAL '3 hours',
        "AtualizadoEm"        = "AtualizadoEm"        - INTERVAL '3 hours',
        "CienciaPreceptorEm"  = "CienciaPreceptorEm"  - INTERVAL '3 hours',
        "DecididoProfessorEm" = "DecididoProfessorEm" - INTERVAL '3 hours';

    INSERT INTO "MigracoesAplicadas" ("Nome") VALUES ('006_fuso_brasilia');
END $migracao$;

-- 3) "RotuloPeriodo" nulo derrubava a listagem de escalas: preenche e passa a exigir o valor
UPDATE "EscalasRodizio" SET "RotuloPeriodo" = '' WHERE "RotuloPeriodo" IS NULL;
UPDATE "EscalasRodizio" SET "TipoAtividade" = 'assistencia' WHERE "TipoAtividade" IS NULL;
UPDATE "EscalasRodizio" SET "Turno" = 'manha' WHERE "Turno" IS NULL;

ALTER TABLE "EscalasRodizio"
    ALTER COLUMN "RotuloPeriodo" SET DEFAULT '',
    ALTER COLUMN "RotuloPeriodo" SET NOT NULL,
    ALTER COLUMN "TipoAtividade" SET DEFAULT 'assistencia',
    ALTER COLUMN "TipoAtividade" SET NOT NULL,
    ALTER COLUMN "Turno" SET NOT NULL;

-- 4) Conferência: o horário mais recente deve bater com o relógio de Brasília
SELECT NOW() AT TIME ZONE 'America/Sao_Paulo' AS "AgoraEmBrasilia",
       MAX("RegistradoEm")                    AS "UltimoPontoRegistrado"
FROM   "RegistrosPresenca";

SELECT to_char("RegistradoEm", 'DD/MM/YYYY HH24:MI') AS "Registro",
       "Tipo", "Status"
FROM   "RegistrosPresenca"
ORDER  BY "RegistradoEm" DESC
LIMIT  5;

COMMIT;
