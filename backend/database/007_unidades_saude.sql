-- =============================================================================
--  Migration 007 – Módulo de Unidades de Saúde
--
--  Estende a tabela "Locais" com o cadastro completo da unidade de saúde e os
--  campos de geocodificação, e cria as tabelas de alocação de estagiários e de
--  cache de geocodificação.
--
--  POR QUE "Locais" E NÃO UMA TABELA NOVA:
--  "Locais" já é a unidade de saúde do sistema — é dela que o check-in lê as
--  coordenadas para validar a presença do aluno (geofence). Criar uma segunda
--  tabela de unidades produziria duas fontes de coordenadas que divergiriam com
--  o tempo, e o check-in continuaria lendo a antiga. Estendendo "Locais", as
--  unidades já cadastradas e os rodízios existentes seguem valendo.
--
--  O script é idempotente: pode ser executado mais de uma vez sem quebrar.
-- =============================================================================

BEGIN;

-- ─────────────────────────────────────────────────────────────────────────────
--  1) Cadastro da unidade de saúde e campos de geocodificação em "Locais"
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE "Locais"
    ADD COLUMN IF NOT EXISTS "Tipo"                  VARCHAR(100)  NULL,
    ADD COLUMN IF NOT EXISTS "Numero"                VARCHAR(20)   NULL,
    ADD COLUMN IF NOT EXISTS "Complemento"           VARCHAR(200)  NULL,
    ADD COLUMN IF NOT EXISTS "Bairro"                VARCHAR(100)  NULL,
    ADD COLUMN IF NOT EXISTS "Cidade"                VARCHAR(100)  NULL,
    ADD COLUMN IF NOT EXISTS "UF"                    VARCHAR(2)    NULL,
    ADD COLUMN IF NOT EXISTS "CEP"                   VARCHAR(10)   NULL,
    ADD COLUMN IF NOT EXISTS "Telefone"              VARCHAR(30)   NULL,
    ADD COLUMN IF NOT EXISTS "Ativo"                 BOOLEAN       NOT NULL DEFAULT TRUE,
    ADD COLUMN IF NOT EXISTS "OrigemCoordenadas"     VARCHAR(30)   NULL,
    ADD COLUMN IF NOT EXISTS "StatusGeocodificacao"  VARCHAR(30)   NULL,
    ADD COLUMN IF NOT EXISTS "EnderecoGeocodificado" TEXT          NULL,
    ADD COLUMN IF NOT EXISTS "PrecisaoLocalizacao"   VARCHAR(100)  NULL,
    ADD COLUMN IF NOT EXISTS "GeocodificadoEm"       TIMESTAMP     NULL,
    ADD COLUMN IF NOT EXISTS "LoteImportacao"        UUID          NULL,
    ADD COLUMN IF NOT EXISTS "AtualizadoEm"          TIMESTAMP     NOT NULL DEFAULT NOW();

-- Valores controlados, para o banco recusar um status escrito errado.
ALTER TABLE "Locais" DROP CONSTRAINT IF EXISTS "CK_Locais_StatusGeocodificacao";
ALTER TABLE "Locais"
    ADD CONSTRAINT "CK_Locais_StatusGeocodificacao"
    CHECK ("StatusGeocodificacao" IS NULL OR "StatusGeocodificacao" IN
        ('pendente', 'processando', 'sucesso', 'nao_encontrado', 'erro', 'revisao_manual'));

ALTER TABLE "Locais" DROP CONSTRAINT IF EXISTS "CK_Locais_OrigemCoordenadas";
ALTER TABLE "Locais"
    ADD CONSTRAINT "CK_Locais_OrigemCoordenadas"
    CHECK ("OrigemCoordenadas" IS NULL OR "OrigemCoordenadas" IN ('NOMINATIM', 'MANUAL', 'OUTRO'));

-- Índices dos filtros da tela de unidades.
CREATE INDEX IF NOT EXISTS "IX_Locais_Nome"                 ON "Locais" ("Nome");
CREATE INDEX IF NOT EXISTS "IX_Locais_CEP"                  ON "Locais" ("CEP");
CREATE INDEX IF NOT EXISTS "IX_Locais_Cidade"               ON "Locais" ("Cidade");
CREATE INDEX IF NOT EXISTS "IX_Locais_Ativo"                ON "Locais" ("Ativo");
CREATE INDEX IF NOT EXISTS "IX_Locais_StatusGeocodificacao" ON "Locais" ("StatusGeocodificacao");
CREATE INDEX IF NOT EXISTS "IX_Locais_LoteImportacao"       ON "Locais" ("LoteImportacao");

-- ─────────────────────────────────────────────────────────────────────────────
--  2) Situação das unidades já cadastradas
--     Quem já tem coordenada foi cadastrada à mão (ou veio do CNES) antes deste
--     módulo: marcamos como MANUAL para que nenhuma importação futura sobrescreva
--     uma coordenada que já está validando check-in hoje.
-- ─────────────────────────────────────────────────────────────────────────────
UPDATE "Locais"
SET "StatusGeocodificacao" = 'sucesso',
    "OrigemCoordenadas"    = COALESCE("OrigemCoordenadas", 'MANUAL')
WHERE "StatusGeocodificacao" IS NULL
  AND ("Latitude" <> 0 OR "Longitude" <> 0);

UPDATE "Locais"
SET "StatusGeocodificacao" = 'pendente'
WHERE "StatusGeocodificacao" IS NULL;

-- Sem cidade preenchida a geocodificação não tem contexto; o DF é o caso do sistema.
UPDATE "Locais"
SET "Cidade" = COALESCE("Cidade", 'Brasília'),
    "UF"     = COALESCE("UF", 'DF')
WHERE "Cidade" IS NULL OR "UF" IS NULL;

-- ─────────────────────────────────────────────────────────────────────────────
--  3) Alocação de estagiários às unidades
--
--     Convive com o rodízio da turma: o rodízio define a escala do grupo, esta
--     tabela registra aluno a aluno em que unidade ele está e desde quando.
--     Trocar de unidade encerra a alocação (DataFim) e cria outra — o histórico
--     é preservado, nunca sobrescrito.
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "AlocacoesEstagiarios" (
    "IdAlocacao"   UUID        PRIMARY KEY,
    "IdUnidade"    UUID        NOT NULL,
    "IdEstagiario" UUID        NOT NULL,
    "DataInicio"   DATE        NOT NULL,
    "DataFim"      DATE        NULL,
    "Ativo"        BOOLEAN     NOT NULL DEFAULT TRUE,
    "Observacao"   TEXT        NULL,
    "CriadoPorId"  UUID        NULL,
    "CriadoEm"     TIMESTAMP   NOT NULL DEFAULT NOW(),
    "AtualizadoEm" TIMESTAMP   NOT NULL DEFAULT NOW(),

    CONSTRAINT "FK_Alocacoes_Unidade"
        FOREIGN KEY ("IdUnidade")    REFERENCES "Locais"("IdLocal")     ON DELETE RESTRICT,
    CONSTRAINT "FK_Alocacoes_Estagiario"
        FOREIGN KEY ("IdEstagiario") REFERENCES "Usuarios"("IdUsuario") ON DELETE CASCADE,
    CONSTRAINT "FK_Alocacoes_CriadoPor"
        FOREIGN KEY ("CriadoPorId")  REFERENCES "Usuarios"("IdUsuario") ON DELETE SET NULL,

    -- Alocação encerrada precisa de data de término, e vice-versa.
    CONSTRAINT "CK_Alocacoes_DataFim"
        CHECK (("Ativo" = TRUE AND "DataFim" IS NULL) OR ("Ativo" = FALSE AND "DataFim" IS NOT NULL)),
    CONSTRAINT "CK_Alocacoes_Periodo"
        CHECK ("DataFim" IS NULL OR "DataFim" >= "DataInicio")
);

CREATE INDEX IF NOT EXISTS "IX_Alocacoes_IdUnidade"    ON "AlocacoesEstagiarios" ("IdUnidade");
CREATE INDEX IF NOT EXISTS "IX_Alocacoes_IdEstagiario" ON "AlocacoesEstagiarios" ("IdEstagiario");

-- Regra "uma alocação ativa por estagiário" garantida pelo banco, não só pela API.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Alocacoes_EstagiarioAtivo"
    ON "AlocacoesEstagiarios" ("IdEstagiario")
    WHERE "Ativo" = TRUE;

-- ─────────────────────────────────────────────────────────────────────────────
--  4) Cache de geocodificação
--
--     Evita consultar o Nominatim duas vezes pelo mesmo endereço. Reimportar a
--     mesma planilha não deve gerar tráfego novo no serviço público. Os "não
--     encontrado" também entram: não valem uma segunda consulta automática.
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "GeocodificacaoCache" (
    "Id"                  UUID              PRIMARY KEY,
    "EnderecoNormalizado" VARCHAR(500)      NOT NULL,
    "Latitude"            DOUBLE PRECISION  NULL,
    "Longitude"           DOUBLE PRECISION  NULL,
    "EnderecoRetornado"   TEXT              NULL,
    "Precisao"            VARCHAR(100)      NULL,
    "Status"              VARCHAR(30)       NOT NULL DEFAULT 'pendente',
    "Provedor"            VARCHAR(30)       NOT NULL DEFAULT 'NOMINATIM',
    "CriadoEm"            TIMESTAMP         NOT NULL DEFAULT NOW(),
    "AtualizadoEm"        TIMESTAMP         NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_GeocodificacaoCache_Endereco"
    ON "GeocodificacaoCache" ("EnderecoNormalizado");

-- ─────────────────────────────────────────────────────────────────────────────
--  5) Conferência
-- ─────────────────────────────────────────────────────────────────────────────
SELECT COALESCE("StatusGeocodificacao", '(nulo)') AS "Status",
       COUNT(*)                                   AS "Unidades"
FROM   "Locais"
GROUP  BY "StatusGeocodificacao"
ORDER  BY "Status";

SELECT COUNT(*) AS "UnidadesAtivas",
       COUNT(*) FILTER (WHERE "Latitude" <> 0 OR "Longitude" <> 0) AS "ComCoordenadas"
FROM   "Locais"
WHERE  "Ativo" = TRUE;

SELECT COUNT(*) AS "AlocacoesAtivas" FROM "AlocacoesEstagiarios" WHERE "Ativo" = TRUE;

COMMIT;
