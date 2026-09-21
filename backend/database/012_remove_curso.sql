-- =============================================================================
--  Migration 012 – Remoção do curso
--
--  O QUE MUDA:
--  Sai o campo "Curso" do aluno e a abrangência "curso" das exceções de
--  calendário. Restam quatro abrangências: faculdade, turma, rodízio e aluno.
--
--  POR QUE:
--  O EstágioCheck atende um curso só — Enfermagem. Com um único curso,
--  "curso" e "faculdade" alcançam exatamente as mesmas pessoas: a abrangência
--  não distinguia nada e ainda exigia que o professor mantivesse o curso
--  escrito igual em todo aluno, sob pena de a exceção não casar.
--
--  DADOS EXISTENTES:
--  A conversão de 'curso' para 'faculdade' abaixo é o caminho certo justamente
--  porque as duas são equivalentes com um curso só — nenhuma exceção perde
--  alcance. Na aplicação desta migration não havia nenhuma linha assim nem em
--  produção nem em homologação; o UPDATE existe para o caso de alguma ter sido
--  criada entre a conferência e a execução.
--
--  IDEMPOTENTE: pode ser executada mais de uma vez sem erro.
-- =============================================================================

BEGIN;

-- 1) Exceções de curso viram exceções de faculdade — mesmo alcance, já que o
--    sistema atende um curso só.
UPDATE "public"."ExcecoesCalendario"
   SET "Abrangencia" = 'faculdade'
 WHERE "Abrangencia" = 'curso';

-- 2) As travas precisam ser recriadas antes de a coluna sair: a CK_Excecoes_Alvo
--    referencia "Curso", e a CK_Excecoes_Abrangencia ainda aceita 'curso'.
ALTER TABLE "public"."ExcecoesCalendario" DROP CONSTRAINT IF EXISTS "CK_Excecoes_Abrangencia";
ALTER TABLE "public"."ExcecoesCalendario"
    ADD CONSTRAINT "CK_Excecoes_Abrangencia"
    CHECK ("Abrangencia" IN ('faculdade', 'turma', 'rodizio', 'aluno'));

-- Cada abrangência exige o seu alvo: sem essa trava, uma exceção de turma sem
-- turma alcançaria a faculdade inteira sem que ninguém percebesse.
ALTER TABLE "public"."ExcecoesCalendario" DROP CONSTRAINT IF EXISTS "CK_Excecoes_Alvo";
ALTER TABLE "public"."ExcecoesCalendario"
    ADD CONSTRAINT "CK_Excecoes_Alvo" CHECK (
        ("Abrangencia" = 'faculdade')
     OR ("Abrangencia" = 'turma'   AND "IdGrupo"     IS NOT NULL)
     OR ("Abrangencia" = 'rodizio' AND "IdEscala"    IS NOT NULL)
     OR ("Abrangencia" = 'aluno'   AND "IdEstudante" IS NOT NULL)
    );

-- 3) As colunas saem por último, já sem nada apontando para elas.
ALTER TABLE "public"."ExcecoesCalendario" DROP COLUMN IF EXISTS "Curso";
ALTER TABLE "public"."Usuarios"           DROP COLUMN IF EXISTS "Curso";

COMMIT;
