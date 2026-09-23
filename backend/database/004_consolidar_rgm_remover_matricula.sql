-- 004 – consolida a matrícula no RGM e remove "Matricula". Sem a coluna, não faz nada.

BEGIN;

CREATE TABLE IF NOT EXISTS "MigracoesAplicadas" (
    "Nome"       TEXT        PRIMARY KEY,
    "AplicadaEm" TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo')
);

DO $migracao$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema = 'public'
                     AND table_name   = 'Usuarios'
                     AND column_name  = 'Matricula') THEN
        RAISE NOTICE '004: coluna "Matricula" já removida — nada a fazer.';
        RETURN;
    END IF;

    -- Migra a matrícula para o RGM apenas em ALUNOS que ainda não têm RGM.
    -- (Supervisores tinham matrícula de teste duplicada; foi descartada.)
    UPDATE "Usuarios"
    SET "Rgm" = "Matricula"
    WHERE "Papel" = 'aluno'
      AND ("Rgm" IS NULL OR "Rgm" = '')
      AND "Matricula" IS NOT NULL AND "Matricula" <> '';

    ALTER TABLE "Usuarios" DROP COLUMN "Matricula";
END $migracao$;

INSERT INTO "MigracoesAplicadas" ("Nome") VALUES ('004_consolidar_rgm_remover_matricula')
ON CONFLICT ("Nome") DO NOTHING;

COMMIT;
