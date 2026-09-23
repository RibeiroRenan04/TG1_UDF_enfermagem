-- 002 – colunas UDF em "Users", códigos de redefinição de senha, histórico de semestre e CNES.
-- Usa os nomes em inglês, que só existem antes do 003; sem "Users", não faz nada.

BEGIN;

CREATE TABLE IF NOT EXISTS "MigracoesAplicadas" (
    "Nome"       TEXT        PRIMARY KEY,
    "AplicadaEm" TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo')
);

DO $migracao$
BEGIN
    IF to_regclass('public."Users"') IS NULL THEN
        RAISE NOTICE '002: tabela "Users" não existe (003 já aplicado) — nada a fazer.';
        RETURN;
    END IF;

    ALTER TABLE "Users"
        ALTER COLUMN "Email" DROP NOT NULL,
        ADD COLUMN IF NOT EXISTS "Rgm"                VARCHAR(50)      NULL,
        ADD COLUMN IF NOT EXISTS "Semester"           INTEGER          NULL,
        ADD COLUMN IF NOT EXISTS "Shift"              VARCHAR(10)      NULL,
        ADD COLUMN IF NOT EXISTS "Institution"        VARCHAR(200)     NULL,
        ADD COLUMN IF NOT EXISTS "MustChangePassword" BOOLEAN          NOT NULL DEFAULT FALSE,
        ADD COLUMN IF NOT EXISTS "MustSetEmail"       BOOLEAN          NOT NULL DEFAULT FALSE,
        ADD COLUMN IF NOT EXISTS "IsActive"           BOOLEAN          NOT NULL DEFAULT TRUE;

    -- Índice único parcial (RGM só quando preenchido)
    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Rgm"
        ON "Users" ("Rgm")
        WHERE "Rgm" IS NOT NULL;

    -- Ajusta índice único de email para parcial (email pode ser NULL para alunos importados)
    DROP INDEX IF EXISTS "IX_Users_Email";
    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email"
        ON "Users" ("Email")
        WHERE "Email" IS NOT NULL;

    CREATE TABLE IF NOT EXISTS "PasswordResetCodes" (
        "Id"        SERIAL          PRIMARY KEY,
        "Email"     VARCHAR(255)    NOT NULL,
        "Code"      VARCHAR(6)      NOT NULL,
        "ExpiresAt" TIMESTAMPTZ     NOT NULL,
        "Used"      BOOLEAN         NOT NULL DEFAULT FALSE,
        "CreatedAt" TIMESTAMPTZ     NOT NULL DEFAULT NOW()
    );

    CREATE INDEX IF NOT EXISTS "IX_PasswordResetCodes_Email_Code"
        ON "PasswordResetCodes" ("Email", "Code");

    CREATE TABLE IF NOT EXISTS "StudentSemesterHistories" (
        "Id"         SERIAL          PRIMARY KEY,
        "StudentId"  UUID            NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
        "Semester"   INTEGER         NOT NULL,
        "TotalHours" NUMERIC(10, 2)  NOT NULL DEFAULT 0,
        "RecordedAt" TIMESTAMPTZ     NOT NULL DEFAULT NOW()
    );

    CREATE INDEX IF NOT EXISTS "IX_StudentSemesterHistories_StudentId"
        ON "StudentSemesterHistories" ("StudentId");

    ALTER TABLE "Locations"
        ADD COLUMN IF NOT EXISTS "CodigoCnes" VARCHAR(20) NULL;

    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Locations_CodigoCnes"
        ON "Locations" ("CodigoCnes")
        WHERE "CodigoCnes" IS NOT NULL;
END $migracao$;

INSERT INTO "MigracoesAplicadas" ("Nome") VALUES ('002_udf_features')
ON CONFLICT ("Nome") DO NOTHING;

COMMIT;
