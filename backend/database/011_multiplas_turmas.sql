-- 011 – aluno em mais de uma turma: o que não se repete é o par (aluno, turma). A agenda
-- impossível continua barrada pela API (ConflitoTurmasService).

BEGIN;

CREATE TABLE IF NOT EXISTS "MigracoesAplicadas" (
    "Nome"       TEXT        PRIMARY KEY,
    "AplicadaEm" TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo')
);

-- A 003 não renomeou os índices: bancos provisionados por caminhos diferentes têm um ou outro nome.
DROP INDEX IF EXISTS "public"."IX_GroupMemberships_StudentId";
DROP INDEX IF EXISTS "public"."IX_MembrosGrupo_IdEstudante";

-- Remove pares duplicados antes do índice único, mantendo o vínculo mais antigo.
DELETE FROM "public"."MembrosGrupo" m
USING "public"."MembrosGrupo" outro
WHERE m."IdEstudante" = outro."IdEstudante"
  AND m."IdGrupo"     = outro."IdGrupo"
  AND (m."CriadoEm" > outro."CriadoEm"
       OR (m."CriadoEm" = outro."CriadoEm" AND m."IdMembroGrupo" > outro."IdMembroGrupo"));

CREATE UNIQUE INDEX IF NOT EXISTS "IX_MembrosGrupo_IdEstudante_IdGrupo"
    ON "public"."MembrosGrupo" ("IdEstudante", "IdGrupo");

CREATE INDEX IF NOT EXISTS "IX_MembrosGrupo_IdEstudante"
    ON "public"."MembrosGrupo" ("IdEstudante");

INSERT INTO "MigracoesAplicadas" ("Nome") VALUES ('011_multiplas_turmas')
ON CONFLICT ("Nome") DO NOTHING;

COMMIT;
