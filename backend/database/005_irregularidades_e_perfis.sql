-- 005 – irregularidades, permissão de atraso, perfil "coordenadora", RGM sem o "14" e termo de
-- responsabilidade. A remoção do "14" (3.2) e o backfill (5) só rodam na primeira execução:
-- repetir a 3.2 cortaria o "14" de um RGM legítimo.

BEGIN;

-- 0) Controle de execução
CREATE TABLE IF NOT EXISTS "MigracoesAplicadas" (
    "Nome"       TEXT        PRIMARY KEY,
    "AplicadaEm" TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo')
);

CREATE TEMP TABLE "_Migracao005" ON COMMIT DROP AS
SELECT NOT EXISTS (SELECT 1 FROM "MigracoesAplicadas" WHERE "Nome" = '005_irregularidades_e_perfis')
       AND to_regclass('public."Irregularidades"') IS NULL AS "PrimeiraExecucao";

-- 1) Novas colunas em "Usuarios"
ALTER TABLE "Usuarios"
    ADD COLUMN IF NOT EXISTS "PermissaoAtraso"  BOOLEAN     NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS "ObservacaoAtraso" TEXT        NULL,
    ADD COLUMN IF NOT EXISTS "TermoAceitoEm"    TIMESTAMP   NULL;

-- 2) Perfil "coordenadora" (o bloqueio de escrita é feito na API). O 014 o renomeia para
--    "secretaria": se a trava já aceita "secretaria", o 014 rodou e ela não é recriada aqui.
DO $$
DECLARE
    v_constraint TEXT;
    v_definicao  TEXT;
BEGIN
    SELECT con.conname, pg_get_constraintdef(con.oid) INTO v_constraint, v_definicao
    FROM   pg_constraint con
    JOIN   pg_class      rel ON rel.oid = con.conrelid
    WHERE  rel.relname = 'Usuarios'
      AND  con.contype = 'c'
      AND  pg_get_constraintdef(con.oid) ILIKE '%Papel%'
    LIMIT  1;

    IF v_definicao ILIKE '%secretaria%' THEN
        RETURN;
    END IF;

    IF v_constraint IS NOT NULL THEN
        EXECUTE format('ALTER TABLE "Usuarios" DROP CONSTRAINT %I', v_constraint);
    END IF;

    ALTER TABLE "Usuarios"
        ADD CONSTRAINT "CK_Usuarios_Papel"
        CHECK ("Papel" IN ('aluno', 'preceptor', 'supervisor', 'coordenadora'));
END $$;

-- 3) RGM sem o "14" do início
--    A senha inicial de quem ainda não acessou continua sendo o RGM antigo: reimporte a
--    planilha da turma depois deste script para a API regravá-la.

-- 3.1) Conferência prévia: colisões que impediriam a remoção do prefixo.
SELECT u."IdUsuario", u."NomeCompleto", u."Rgm" AS "RgmAtual",
       substring(u."Rgm" FROM 3) AS "RgmNovo"
FROM   "Usuarios" u
WHERE  u."Papel" = 'aluno'
  AND  u."Rgm" LIKE '14%'
  AND  length(u."Rgm") > 2
  AND  EXISTS (
         SELECT 1 FROM "Usuarios" x
         WHERE  x."Rgm" = substring(u."Rgm" FROM 3)
           AND  x."IdUsuario" <> u."IdUsuario"
       );

-- 3.2) Remoção do prefixo
UPDATE "Usuarios" u
SET    "Rgm"          = substring(u."Rgm" FROM 3),
       "AtualizadoEm" = (NOW() AT TIME ZONE 'America/Sao_Paulo')
WHERE  (SELECT "PrimeiraExecucao" FROM "_Migracao005")
  AND  u."Papel" = 'aluno'
  AND  u."Rgm" LIKE '14%'
  AND  length(u."Rgm") > 2
  AND  NOT EXISTS (
         SELECT 1 FROM "Usuarios" x
         WHERE  x."Rgm" = substring(u."Rgm" FROM 3)
           AND  x."IdUsuario" <> u."IdUsuario"
       );

-- 4) Irregularidades: aguardando_preceptor → aguardando_professor → aprovada | negada
CREATE TABLE IF NOT EXISTS "Irregularidades" (
    "IdIrregularidade"     UUID         PRIMARY KEY,
    "IdEstudante"          UUID         NOT NULL,
    "IdPresenca"           UUID         NULL,
    "IdEscala"             UUID         NULL,
    "Tipo"                 VARCHAR(30)  NOT NULL DEFAULT 'outro',
    "DataOcorrencia"       DATE         NOT NULL,
    "Descricao"            TEXT         NOT NULL,
    "Status"               VARCHAR(30)  NOT NULL DEFAULT 'aguardando_preceptor',

    "IdPreceptor"          UUID         NULL,
    "ObservacaoPreceptor"  TEXT         NULL,
    "CienciaPreceptorEm"   TIMESTAMP    NULL,

    "IdProfessor"          UUID         NULL,
    "ParecerProfessor"     TEXT         NULL,
    "DecididoProfessorEm"  TIMESTAMP    NULL,

    "CriadoEm"             TIMESTAMP    NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo'),
    "AtualizadoEm"         TIMESTAMP    NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo'),

    CONSTRAINT "FK_Irregularidades_Estudante"
        FOREIGN KEY ("IdEstudante")  REFERENCES "Usuarios"("IdUsuario")        ON DELETE CASCADE,
    CONSTRAINT "FK_Irregularidades_Presenca"
        FOREIGN KEY ("IdPresenca")   REFERENCES "RegistrosPresenca"("IdPresenca") ON DELETE SET NULL,
    CONSTRAINT "FK_Irregularidades_Escala"
        FOREIGN KEY ("IdEscala")     REFERENCES "EscalasRodizio"("IdEscala")   ON DELETE SET NULL,
    CONSTRAINT "FK_Irregularidades_Preceptor"
        FOREIGN KEY ("IdPreceptor")  REFERENCES "Usuarios"("IdUsuario")        ON DELETE SET NULL,
    CONSTRAINT "FK_Irregularidades_Professor"
        FOREIGN KEY ("IdProfessor")  REFERENCES "Usuarios"("IdUsuario")        ON DELETE SET NULL,

    CONSTRAINT "CK_Irregularidades_Status"
        CHECK ("Status" IN ('aguardando_preceptor', 'aguardando_professor', 'aprovada', 'negada')),
    CONSTRAINT "CK_Irregularidades_Tipo"
        CHECK ("Tipo" IN ('atraso', 'esquecimento_checkin', 'esquecimento_checkout',
                          'fora_do_local', 'falta_justificada', 'problema_tecnico', 'outro'))
);

CREATE INDEX IF NOT EXISTS "IX_Irregularidades_Status"
    ON "Irregularidades" ("Status");
CREATE INDEX IF NOT EXISTS "IX_Irregularidades_IdEstudante"
    ON "Irregularidades" ("IdEstudante");
CREATE INDEX IF NOT EXISTS "IX_Irregularidades_IdPresenca"
    ON "Irregularidades" ("IdPresenca");

-- 5) Backfill dos pontos já irregulares (o "- 3 horas" assume ponto em UTC, estado anterior ao 006)
INSERT INTO "Irregularidades" (
    "IdIrregularidade", "IdEstudante", "IdPresenca", "IdEscala",
    "Tipo", "DataOcorrencia", "Descricao", "Status", "CriadoEm", "AtualizadoEm"
)
SELECT gen_random_uuid(),
       r."IdEstudante",
       r."IdPresenca",
       r."IdEscala",
       'fora_do_local',
       (r."RegistradoEm" - INTERVAL '3 hours')::date,
       COALESCE(NULLIF(r."MotivoIrregularidade", ''), 'Registro de ponto fora das regras.'),
       'aguardando_preceptor',
       (NOW() AT TIME ZONE 'America/Sao_Paulo'),
       (NOW() AT TIME ZONE 'America/Sao_Paulo')
FROM   "RegistrosPresenca" r
WHERE  (SELECT "PrimeiraExecucao" FROM "_Migracao005")
  AND  r."Status" = 'irregular'
  AND  NOT EXISTS (
         SELECT 1 FROM "Irregularidades" i
         WHERE  i."IdPresenca" = r."IdPresenca"
       );

INSERT INTO "MigracoesAplicadas" ("Nome") VALUES ('005_irregularidades_e_perfis')
ON CONFLICT ("Nome") DO NOTHING;

-- 6) Conferência
SELECT "Papel", COUNT(*) AS "Usuarios"
FROM   "Usuarios"
GROUP  BY "Papel"
ORDER  BY "Papel";

SELECT COUNT(*) FILTER (WHERE "Rgm" LIKE '14%') AS "RgmsAindaCom14",
       COUNT(*)                                 AS "TotalAlunosComRgm"
FROM   "Usuarios"
WHERE  "Papel" = 'aluno' AND "Rgm" IS NOT NULL;

SELECT "Status", COUNT(*) AS "Ocorrencias"
FROM   "Irregularidades"
GROUP  BY "Status"
ORDER  BY "Status";

COMMIT;
