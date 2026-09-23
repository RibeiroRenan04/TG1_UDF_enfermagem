-- 013 – todos os horários em Brasília (GMT-3). Converte os carimbos que o backend gravava em UTC
-- (usuários, turmas, vínculos, rodízios, avaliações, acompanhamentos), troca TIMESTAMPTZ por
-- TIMESTAMP e corrige os DEFAULT NOW(). O passo 1 não é idempotente (subtrairia 6 h): só roda
-- se o 013 não consta em "MigracoesAplicadas". Aplique junto com o deploy do backend.

BEGIN;

-- 0) Controle de execução (a conversão do passo 1 não é idempotente)
CREATE TABLE IF NOT EXISTS "MigracoesAplicadas" (
    "Nome"       TEXT        PRIMARY KEY,
    "AplicadaEm" TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo')
);

-- 1) Carimbos gravados em UTC → Brasília
DO $migracao$
BEGIN
    IF EXISTS (SELECT 1 FROM "MigracoesAplicadas" WHERE "Nome" = '013_horarios_brasilia') THEN
        RAISE NOTICE '013: conversão de fuso já aplicada neste banco — pulando o passo 1.';
        RETURN;
    END IF;

    UPDATE "Usuarios"
    SET "CriadoEm"      = "CriadoEm"      - INTERVAL '3 hours',
        "AtualizadoEm"  = "AtualizadoEm"  - INTERVAL '3 hours',
        "TermoAceitoEm" = "TermoAceitoEm" - INTERVAL '3 hours';

    UPDATE "GruposEstudantes" SET "CriadoEm" = "CriadoEm" - INTERVAL '3 hours';
    UPDATE "MembrosGrupo"     SET "CriadoEm" = "CriadoEm" - INTERVAL '3 hours';
    UPDATE "EscalasRodizio"   SET "CriadoEm" = "CriadoEm" - INTERVAL '3 hours';
    UPDATE "Avaliacoes"       SET "CriadoEm" = "CriadoEm" - INTERVAL '3 hours';

    UPDATE "AcompanhamentosFormativos"
    SET "CriadoEm"            = "CriadoEm"            - INTERVAL '3 hours',
        "AtualizadoEm"        = "AtualizadoEm"        - INTERVAL '3 hours',
        "AssinadoPreceptorEm" = "AssinadoPreceptorEm" - INTERVAL '3 hours',
        "AssinadoEstudanteEm" = "AssinadoEstudanteEm" - INTERVAL '3 hours';
END $migracao$;

-- 2) TIMESTAMPTZ → TIMESTAMP ("ExpiraEm" é comparado com BrasiliaTime.Agora na API)
DO $migracao$
DECLARE
    v_coluna RECORD;
BEGIN
    FOR v_coluna IN
        SELECT table_name, column_name
        FROM   information_schema.columns
        WHERE  table_schema = 'public'
          AND  data_type    = 'timestamp with time zone'
          AND  (table_name, column_name) IN (('CodigosRedefinicaoSenha',    'ExpiraEm'),
                                             ('CodigosRedefinicaoSenha',    'CriadoEm'),
                                             ('HistoricoSemestreEstudante', 'RegistradoEm'))
    LOOP
        EXECUTE format(
            'ALTER TABLE %I ALTER COLUMN %I TYPE TIMESTAMP USING %I AT TIME ZONE %L',
            v_coluna.table_name, v_coluna.column_name, v_coluna.column_name, 'America/Sao_Paulo');
    END LOOP;
END $migracao$;

-- 3) DEFAULT NOW() → horário de Brasília (NOW() em TIMESTAMP grava o relógio do servidor, UTC)
DO $migracao$
DECLARE
    v_coluna RECORD;
BEGIN
    FOR v_coluna IN
        SELECT table_name, column_name
        FROM   information_schema.columns
        WHERE  table_schema   = 'public'
          AND  data_type      = 'timestamp without time zone'
          AND  lower(column_default) IN ('now()', 'current_timestamp')
    LOOP
        EXECUTE format(
            'ALTER TABLE %I ALTER COLUMN %I SET DEFAULT (NOW() AT TIME ZONE %L)',
            v_coluna.table_name, v_coluna.column_name, 'America/Sao_Paulo');
    END LOOP;
END $migracao$;

INSERT INTO "MigracoesAplicadas" ("Nome") VALUES ('013_horarios_brasilia')
ON CONFLICT ("Nome") DO NOTHING;

-- 4) Conferência
-- Não pode voltar nenhuma linha: todo carimbo do schema é TIMESTAMP sem fuso.
SELECT table_name, column_name
FROM   information_schema.columns
WHERE  table_schema = 'public'
  AND  data_type    = 'timestamp with time zone';

-- Não pode voltar nenhuma linha: todo default de data usa o horário de Brasília.
SELECT table_name, column_name, column_default
FROM   information_schema.columns
WHERE  table_schema   = 'public'
  AND  lower(column_default) IN ('now()', 'current_timestamp');

-- O último cadastro/assinatura deve estar no passado próximo do relógio de Brasília.
SELECT NOW() AT TIME ZONE 'America/Sao_Paulo' AS "AgoraEmBrasilia",
       (SELECT MAX("AtualizadoEm") FROM "Usuarios")                  AS "UltimoUsuarioAtualizado",
       (SELECT MAX("AtualizadoEm") FROM "AcompanhamentosFormativos") AS "UltimoAcompanhamento";

COMMIT;
