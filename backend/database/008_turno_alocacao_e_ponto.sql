-- =============================================================================
--  Migration 008 – Alocação por turno e travas do ponto
--
--  Reunião de 06/09/2026 (ata da equipe de desenvolvimento e QA):
--
--  • ALOCAÇÃO POR TURNO
--    O aluno passa a poder estagiar em turnos diferentes (manhã em uma unidade,
--    tarde em outra), mas nunca duas vezes no MESMO turno. A tabela
--    "AlocacoesEstagiarios" ganha a coluna "Turno" e o índice único que garantia
--    "uma alocação ativa por estagiário" passa a ser "uma por estagiário e turno".
--
--  • PONTO: 1 CHECK-IN E 1 CHECK-OUT POR TURNO
--    A regra é aplicada na API (AttendanceController). Aqui apenas criamos os
--    índices que a consulta usa — não há coluna nova em "RegistrosPresenca":
--    o turno do registro vem da escala vinculada ou do horário do ponto.
--
--  • IRREGULARIDADES: TRAVA DE DUPLICIDADE
--    Um ponto tem uma contestação por vez enquanto ela não for negada. A regra é
--    aplicada na API; o índice abaixo torna a checagem barata.
--
--  Compatível com PostgreSQL (Supabase / Railway). O script é idempotente:
--  pode ser executado mais de uma vez sem quebrar.
-- =============================================================================

BEGIN;

-- ─────────────────────────────────────────────────────────────────────────────
--  1) Turno da alocação de estagiários
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE "AlocacoesEstagiarios"
    ADD COLUMN IF NOT EXISTS "Turno" VARCHAR(10) NOT NULL DEFAULT 'manha';

-- As alocações que já existiam herdam o turno cadastrado do próprio aluno.
-- Sem turno no cadastro, ficam na manhã (o padrão da coluna).
UPDATE "AlocacoesEstagiarios" a
SET    "Turno" = u."Turno"
FROM   "Usuarios" u
WHERE  u."IdUsuario" = a."IdEstagiario"
  AND  u."Turno" IN ('manha', 'tarde', 'noite')
  AND  a."Turno" IS DISTINCT FROM u."Turno";

-- Só aceita os três turnos do estágio.
ALTER TABLE "AlocacoesEstagiarios"
    DROP CONSTRAINT IF EXISTS "CK_Alocacoes_Turno";
ALTER TABLE "AlocacoesEstagiarios"
    ADD CONSTRAINT "CK_Alocacoes_Turno"
    CHECK ("Turno" IN ('manha', 'tarde', 'noite'));

-- ─────────────────────────────────────────────────────────────────────────────
--  2) Duplicidade: uma alocação ativa por estagiário E TURNO
--
--  O índice antigo travava o aluno em uma única unidade, o que impedia o caso
--  legítimo de manhã em um local e tarde em outro. O novo mantém a proibição
--  onde ela importa: o mesmo turno duas vezes.
--
--  Antes de criar o índice, encerramos duplicatas do mesmo turno que porventura
--  existam (mantendo a alocação mais recente), senão a criação falharia.
-- ─────────────────────────────────────────────────────────────────────────────
DROP INDEX IF EXISTS "UX_Alocacoes_EstagiarioAtivo";

UPDATE "AlocacoesEstagiarios"
SET    "Ativo"        = FALSE,
       "DataFim"      = COALESCE("DataFim", CURRENT_DATE),
       "AtualizadoEm" = NOW()
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

-- ─────────────────────────────────────────────────────────────────────────────
--  3) Índices de apoio às travas do ponto e da irregularidade
--
--  • O check-in/check-out do turno consulta os registros do aluno no dia.
--  • A trava de duplicidade consulta a irregularidade em aberto de um ponto.
-- ─────────────────────────────────────────────────────────────────────────────
CREATE INDEX IF NOT EXISTS "IX_RegistrosPresenca_EstudanteData"
    ON "RegistrosPresenca" ("IdEstudante", "RegistradoEm");

CREATE INDEX IF NOT EXISTS "IX_Irregularidades_Presenca"
    ON "Irregularidades" ("IdPresenca")
    WHERE "IdPresenca" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_Irregularidades_EstudanteStatus"
    ON "Irregularidades" ("IdEstudante", "Status");

-- ─────────────────────────────────────────────────────────────────────────────
--  4) Conferência
-- ─────────────────────────────────────────────────────────────────────────────
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

-- Alunos em mais de um turno — o caso que a reunião pediu para liberar.
SELECT COUNT(*) AS "AlunosEmMaisDeUmTurno"
FROM (
    SELECT "IdEstagiario"
    FROM   "AlocacoesEstagiarios"
    WHERE  "Ativo" = TRUE
    GROUP  BY "IdEstagiario"
    HAVING COUNT(DISTINCT "Turno") > 1
) AS "MultiTurno";

COMMIT;
