-- 012 – remove o curso do aluno e a abrangência "curso" das exceções. O sistema atende só
-- Enfermagem, então "curso" e "faculdade" alcançavam as mesmas pessoas.

BEGIN;

CREATE TABLE IF NOT EXISTS "MigracoesAplicadas" (
    "Nome"       TEXT        PRIMARY KEY,
    "AplicadaEm" TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo')
);

-- 1) Exceções de curso viram de faculdade (mesmo alcance)
UPDATE "public"."ExcecoesCalendario"
   SET "Abrangencia" = 'faculdade'
 WHERE "Abrangencia" = 'curso';

-- 2) As travas saem antes da coluna: a CK_Excecoes_Alvo referencia "Curso"
ALTER TABLE "public"."ExcecoesCalendario" DROP CONSTRAINT IF EXISTS "CK_Excecoes_Abrangencia";
ALTER TABLE "public"."ExcecoesCalendario"
    ADD CONSTRAINT "CK_Excecoes_Abrangencia"
    CHECK ("Abrangencia" IN ('faculdade', 'turma', 'rodizio', 'aluno'));

ALTER TABLE "public"."ExcecoesCalendario" DROP CONSTRAINT IF EXISTS "CK_Excecoes_Alvo";
ALTER TABLE "public"."ExcecoesCalendario"
    ADD CONSTRAINT "CK_Excecoes_Alvo" CHECK (
        ("Abrangencia" = 'faculdade')
     OR ("Abrangencia" = 'turma'   AND "IdGrupo"     IS NOT NULL)
     OR ("Abrangencia" = 'rodizio' AND "IdEscala"    IS NOT NULL)
     OR ("Abrangencia" = 'aluno'   AND "IdEstudante" IS NOT NULL)
    );

-- 3) Colunas
ALTER TABLE "public"."ExcecoesCalendario" DROP COLUMN IF EXISTS "Curso";
ALTER TABLE "public"."Usuarios"           DROP COLUMN IF EXISTS "Curso";

INSERT INTO "MigracoesAplicadas" ("Nome") VALUES ('012_remove_curso')
ON CONFLICT ("Nome") DO NOTHING;

COMMIT;
