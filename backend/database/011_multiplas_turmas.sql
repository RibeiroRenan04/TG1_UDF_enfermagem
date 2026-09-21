-- =============================================================================
--  Migration 011 – Aluno em mais de uma turma
--
--  O QUE MUDA:
--  "MembrosGrupo" tinha índice ÚNICO em "IdEstudante": um aluno, uma turma.
--  Vincular o aluno a uma turma nova desfazia o vínculo anterior e, com ele, a
--  referência do estágio que ele ainda estava cursando.
--
--  POR QUE:
--  Em estágios de saúde é comum o discente cursar dois módulos ao mesmo tempo
--  (Saúde Coletiva/UBS em um turno e Estágio Hospitalar em outro) ou repor carga
--  horária em uma turma complementar. A substituição automática comprometia o
--  registro de presença e as horas já cumpridas.
--
--  A REGRA NOVA:
--  O que não se repete é o par (aluno, turma). A agenda impossível — mesmo
--  turno, mesmos dias da semana e períodos sobrepostos — continua barrada, mas
--  pela API (ConflitoTurmasService), onde há como explicar o motivo ao usuário.
--
--  IDEMPOTENTE: pode ser executada mais de uma vez sem erro.
-- =============================================================================

-- O índice nasceu como "IX_GroupMemberships_StudentId" na migration base; a 003
-- renomeou a tabela e as colunas, mas não os índices. Os dois nomes são tratados
-- porque bancos provisionados por caminhos diferentes podem ter qualquer um.
DROP INDEX IF EXISTS "public"."IX_GroupMemberships_StudentId";
DROP INDEX IF EXISTS "public"."IX_MembrosGrupo_IdEstudante";

-- Limpa duplicidades antes de criar o índice único, caso o banco já tenha
-- recebido o mesmo par por algum caminho manual. Mantém o vínculo mais antigo.
DELETE FROM "public"."MembrosGrupo" m
USING "public"."MembrosGrupo" outro
WHERE m."IdEstudante" = outro."IdEstudante"
  AND m."IdGrupo"     = outro."IdGrupo"
  AND (m."CriadoEm" > outro."CriadoEm"
       OR (m."CriadoEm" = outro."CriadoEm" AND m."IdMembroGrupo" > outro."IdMembroGrupo"));

CREATE UNIQUE INDEX IF NOT EXISTS "IX_MembrosGrupo_IdEstudante_IdGrupo"
    ON "public"."MembrosGrupo" ("IdEstudante", "IdGrupo");

-- A busca "turmas deste aluno" continua indexada, agora sem exigir unicidade.
CREATE INDEX IF NOT EXISTS "IX_MembrosGrupo_IdEstudante"
    ON "public"."MembrosGrupo" ("IdEstudante");
