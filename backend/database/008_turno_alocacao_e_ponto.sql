-- 008 – alocação por turno (manhã numa unidade, tarde em outra; nunca o mesmo turno duas vezes)
-- e índices das travas de ponto e irregularidade aplicadas na API.

BEGIN;

CREATE TABLE IF NOT EXISTS "MigracoesAplicadas" (
    "Nome"       TEXT        PRIMARY KEY,
    "AplicadaEm" TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo')
);

-- 1) Turno da alocação de estagiários
-- As alocações existentes herdam o turno do cadastro do aluno — só quando a coluna nasce:
-- depois, o turno é escolhido na tela e repetir a herança sobrescreveria as escolhas.
DO $migracao$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'public'
                 AND table_name   = 'AlocacoesEstagiarios'
                 AND column_name  = 'Turno') THEN
        RETURN;
    END IF;

    ALTER TABLE "AlocacoesEstagiarios"
        ADD COLUMN "Turno" VARCHAR(10) NOT NULL DEFAULT 'manha';

    UPDATE "AlocacoesEstagiarios" a
    SET    "Turno" = u."Turno"
    FROM   "Usuarios" u
    WHERE  u."IdUsuario" = a."IdEstagiario"
      AND  u."Turno" IN ('manha', 'tarde', 'noite')
      AND  a."Turno" IS DISTINCT FROM u."Turno";
END $migracao$;

ALTER TABLE "AlocacoesEstagiarios"
    DROP CONSTRAINT IF EXISTS "CK_Alocacoes_Turno";
ALTER TABLE "AlocacoesEstagiarios"
    ADD CONSTRAINT "CK_Alocacoes_Turno"
    CHECK ("Turno" IN ('manha', 'tarde', 'noite'));

-- 2) Uma alocação ativa por estagiário e turno (encerra duplicatas antes de criar o índice)
DROP INDEX IF EXISTS "UX_Alocacoes_EstagiarioAtivo";

UPDATE "AlocacoesEstagiarios"
SET    "Ativo"        = FALSE,
       "DataFim"      = COALESCE("DataFim", (NOW() AT TIME ZONE 'America/Sao_Paulo')::date),
       "AtualizadoEm" = (NOW() AT TIME ZONE 'America/Sao_Paulo')
WHERE  "IdAlocacao" IN (
    SELECT "IdAlocacao"
    FROM (
        SELECT "IdAlocacao",
               ROW_NUMBER() OVER (
                   PARTITION BY "IdEstagiario", "Turno"
                   ORDER BY "DataInicio" DESC, "CriadoEm" DESC
               ) AS "Posicao"
        FROM   "AlocacoesEstagiarios"
        WHERE  "Ativo" = TRUE
    ) AS "Ranqueadas"
    WHERE "Posicao" > 1
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_Alocacoes_EstagiarioTurnoAtivo"
    ON "AlocacoesEstagiarios" ("IdEstagiario", "Turno")
    WHERE "Ativo" = TRUE;

-- 3) Índices de apoio às travas do ponto e da irregularidade
CREATE INDEX IF NOT EXISTS "IX_RegistrosPresenca_EstudanteData"
    ON "RegistrosPresenca" ("IdEstudante", "RegistradoEm");

CREATE INDEX IF NOT EXISTS "IX_Irregularidades_Presenca"
    ON "Irregularidades" ("IdPresenca")
    WHERE "IdPresenca" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_Irregularidades_EstudanteStatus"
    ON "Irregularidades" ("IdEstudante", "Status");

-- 4) Conferência
SELECT "Turno", COUNT(*) AS "AlocacoesAtivas"
FROM   "AlocacoesEstagiarios"
WHERE  "Ativo" = TRUE
GROUP  BY "Turno"
ORDER  BY "Turno";

-- Não pode voltar nenhuma linha: é a regra de duplicidade do mesmo turno.
SELECT "IdEstagiario", "Turno", COUNT(*) AS "Duplicadas"
FROM   "AlocacoesEstagiarios"
WHERE  "Ativo" = TRUE
GROUP  BY "IdEstagiario", "Turno"
HAVING COUNT(*) > 1;

-- Alunos em mais de um turno.
SELECT COUNT(*) AS "AlunosEmMaisDeUmTurno"
FROM (
    SELECT "IdEstagiario"
    FROM   "AlocacoesEstagiarios"
    WHERE  "Ativo" = TRUE
    GROUP  BY "IdEstagiario"
    HAVING COUNT(DISTINCT "Turno") > 1
) AS "MultiTurno";

INSERT INTO "MigracoesAplicadas" ("Nome") VALUES ('008_turno_alocacao_e_ponto')
ON CONFLICT ("Nome") DO NOTHING;

COMMIT;
