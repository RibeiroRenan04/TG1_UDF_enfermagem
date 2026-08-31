-- =============================================================================
--  Migration 005 – Fluxo de irregularidades, permissão de atraso, perfil
--                  "coordenadora", edição de turno, RGM sem o prefixo "14"
--                  e termo de responsabilidade de acesso.
--
--  Compatível com PostgreSQL (Supabase / Railway). O script é idempotente:
--  pode ser executado mais de uma vez sem quebrar.
--
--  Recomendação: rode dentro de uma transação e confira os SELECTs de
--  conferência no fim antes do COMMIT.
-- =============================================================================

BEGIN;

-- ─────────────────────────────────────────────────────────────────────────────
--  1) Novas colunas em "Usuarios"
--     • PermissaoAtraso  – autoriza o aluno a chegar após o início do turno
--                          (a carga horária do dia continua sendo exigida);
--     • ObservacaoAtraso – motivo/registro da autorização, dado pelo professor;
--     • TermoAceitoEm    – aceite do termo de responsabilidade de acesso,
--                          exigido de todo perfil que não é aluno.
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE "Usuarios"
    ADD COLUMN IF NOT EXISTS "PermissaoAtraso"  BOOLEAN     NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS "ObservacaoAtraso" TEXT        NULL,
    ADD COLUMN IF NOT EXISTS "TermoAceitoEm"    TIMESTAMP   NULL;


-- ─────────────────────────────────────────────────────────────────────────────
--  2) Novo perfil "coordenadora"
--     Mesma visão do professor (supervisor), porém somente leitura — o bloqueio
--     de escrita é feito na API ([Authorize(Roles = ...)]) e no frontend.
--     Aqui só liberamos o valor na restrição da coluna "Papel", se ela existir.
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE
    v_constraint TEXT;
BEGIN
    SELECT con.conname INTO v_constraint
    FROM   pg_constraint con
    JOIN   pg_class      rel ON rel.oid = con.conrelid
    WHERE  rel.relname = 'Usuarios'
      AND  con.contype = 'c'
      AND  pg_get_constraintdef(con.oid) ILIKE '%Papel%'
    LIMIT  1;

    IF v_constraint IS NOT NULL THEN
        EXECUTE format('ALTER TABLE "Usuarios" DROP CONSTRAINT %I', v_constraint);
    END IF;
END $$;

ALTER TABLE "Usuarios"
    ADD CONSTRAINT "CK_Usuarios_Papel"
    CHECK ("Papel" IN ('aluno', 'preceptor', 'supervisor', 'coordenadora'));


-- ─────────────────────────────────────────────────────────────────────────────
--  3) RGM sem o "14" do início
--     O prefixo "14" deixou de fazer parte do formato. Removemos o prefixo dos
--     RGMs de alunos que ainda o possuem, desde que o resultado não colida com
--     um RGM já existente (o índice de RGM é único).
--
--     ATENÇÃO: a senha inicial do aluno que ainda NÃO fez o primeiro acesso é o
--     RGM antigo (com o 14). O hash da senha não é alterado aqui — reimporte a
--     planilha da turma depois de rodar este script e a API regrava a senha
--     inicial no formato novo para quem ainda tem "DeveTrocarSenha" = TRUE.
-- ─────────────────────────────────────────────────────────────────────────────

-- 3.1) Conferência prévia: colisões que impediriam a remoção do prefixo.
--      Se esta consulta retornar linhas, resolva os RGMs duplicados antes.
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

-- 3.2) Remoção do prefixo.
UPDATE "Usuarios" u
SET    "Rgm"          = substring(u."Rgm" FROM 3),
       "AtualizadoEm" = NOW()
WHERE  u."Papel" = 'aluno'
  AND  u."Rgm" LIKE '14%'
  AND  length(u."Rgm") > 2
  AND  NOT EXISTS (
         SELECT 1 FROM "Usuarios" x
         WHERE  x."Rgm" = substring(u."Rgm" FROM 3)
           AND  x."IdUsuario" <> u."IdUsuario"
       );


-- ─────────────────────────────────────────────────────────────────────────────
--  4) Tabela "Irregularidades" — fluxo aluno → preceptor → professor
--
--     Situações ("Status"):
--       aguardando_preceptor → o aluno registrou/gerou a ocorrência;
--       aguardando_professor → o preceptor deu ciência e observou, encaminhando;
--       aprovada / negada    → decisão final, exclusiva do professor.
--
--     O preceptor NÃO altera a situação: ele só preenche "ObservacaoPreceptor"
--     e "CienciaPreceptorEm". A decisão fica em "ParecerProfessor".
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "Irregularidades" (
    "IdIrregularidade"     UUID         PRIMARY KEY,
    "IdEstudante"          UUID         NOT NULL,
    "IdPresenca"           UUID         NULL,
    "IdEscala"             UUID         NULL,
    "Tipo"                 VARCHAR(30)  NOT NULL DEFAULT 'outro',
    "DataOcorrencia"       DATE         NOT NULL,
    "Descricao"            TEXT         NOT NULL,
    "Status"               VARCHAR(30)  NOT NULL DEFAULT 'aguardando_preceptor',

    -- Etapa do preceptor: ciência + observação (sem poder decidir)
    "IdPreceptor"          UUID         NULL,
    "ObservacaoPreceptor"  TEXT         NULL,
    "CienciaPreceptorEm"   TIMESTAMP    NULL,

    -- Etapa do professor: decisão final
    "IdProfessor"          UUID         NULL,
    "ParecerProfessor"     TEXT         NULL,
    "DecididoProfessorEm"  TIMESTAMP    NULL,

    "CriadoEm"             TIMESTAMP    NOT NULL DEFAULT NOW(),
    "AtualizadoEm"         TIMESTAMP    NOT NULL DEFAULT NOW(),

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


-- ─────────────────────────────────────────────────────────────────────────────
--  5) Backfill: abre a ocorrência dos registros de ponto que já estão
--     irregulares e ainda não têm irregularidade vinculada, para que entrem
--     no novo fluxo de análise.
-- ─────────────────────────────────────────────────────────────────────────────
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
       NOW(),
       NOW()
FROM   "RegistrosPresenca" r
WHERE  r."Status" = 'irregular'
  AND  NOT EXISTS (
         SELECT 1 FROM "Irregularidades" i
         WHERE  i."IdPresenca" = r."IdPresenca"
       );


-- ─────────────────────────────────────────────────────────────────────────────
--  6) Conferência
-- ─────────────────────────────────────────────────────────────────────────────
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


-- =============================================================================
--  PÓS-MIGRAÇÃO (opcional) — cadastro do primeiro usuário "coordenadora".
--  Prefira criar pela tela de Usuários do professor; o SQL abaixo fica como
--  alternativa. Troque o e-mail e gere o hash BCrypt pela própria aplicação
--  (o campo "DeveTrocarSenha" força a definição de senha no primeiro acesso).
-- =============================================================================
-- UPDATE "Usuarios"
-- SET    "Papel" = 'coordenadora', "AtualizadoEm" = NOW()
-- WHERE  "Email" = 'coordenacao@cs.udf.edu.br';
