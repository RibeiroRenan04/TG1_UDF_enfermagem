-- 014 – o perfil de consulta "coordenadora" passa a se chamar "secretaria". Mesmas permissões
-- (visão do professor, somente leitura). Quem estiver logado como coordenadora precisa entrar
-- de novo: o token antigo carrega o papel antigo. Aplique junto com o deploy do backend.

BEGIN;

-- 0) Controle de execução
CREATE TABLE IF NOT EXISTS "MigracoesAplicadas" (
    "Nome"       TEXT        PRIMARY KEY,
    "AplicadaEm" TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo')
);

-- 1) A trava sai antes do UPDATE: a atual não aceita "secretaria"
ALTER TABLE "Usuarios" DROP CONSTRAINT IF EXISTS "CK_Usuarios_Papel";

-- 2) Usuários
UPDATE "Usuarios"
SET    "Papel" = 'secretaria', "AtualizadoEm" = (NOW() AT TIME ZONE 'America/Sao_Paulo')
WHERE  "Papel" = 'coordenadora';

-- 3) Trava com o novo papel
ALTER TABLE "Usuarios"
    ADD CONSTRAINT "CK_Usuarios_Papel"
    CHECK ("Papel" IN ('aluno', 'preceptor', 'supervisor', 'secretaria'));

INSERT INTO "MigracoesAplicadas" ("Nome") VALUES ('014_perfil_secretaria')
ON CONFLICT ("Nome") DO NOTHING;

-- 4) Conferência
SELECT "Papel", COUNT(*) AS "Usuarios"
FROM   "Usuarios"
GROUP  BY "Papel"
ORDER  BY "Papel";

COMMIT;

-- Opcional: primeiro usuário "secretaria" (prefira a tela de Usuários).
-- UPDATE "Usuarios"
-- SET    "Papel" = 'secretaria', "AtualizadoEm" = (NOW() AT TIME ZONE 'America/Sao_Paulo')
-- WHERE  "Email" = 'secretaria@cs.udf.edu.br';
