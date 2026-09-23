-- 007 – módulo de Unidades de Saúde: estende "Locais" (é dela que o check-in lê as coordenadas;
-- uma segunda tabela divergiria) e cria alocações de estagiários e cache de geocodificação.

BEGIN;

CREATE TABLE IF NOT EXISTS "MigracoesAplicadas" (
    "Nome"       TEXT        PRIMARY KEY,
    "AplicadaEm" TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo')
);

-- 1) Cadastro da unidade de saúde e campos de geocodificação em "Locais"
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
    ADD COLUMN IF NOT EXISTS "AtualizadoEm"          TIMESTAMP     NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo');

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

CREATE INDEX IF NOT EXISTS "IX_Locais_Nome"                 ON "Locais" ("Nome");
CREATE INDEX IF NOT EXISTS "IX_Locais_CEP"                  ON "Locais" ("CEP");
CREATE INDEX IF NOT EXISTS "IX_Locais_Cidade"               ON "Locais" ("Cidade");
CREATE INDEX IF NOT EXISTS "IX_Locais_Ativo"                ON "Locais" ("Ativo");
CREATE INDEX IF NOT EXISTS "IX_Locais_StatusGeocodificacao" ON "Locais" ("StatusGeocodificacao");
CREATE INDEX IF NOT EXISTS "IX_Locais_LoteImportacao"       ON "Locais" ("LoteImportacao");

-- 2) Unidades que já tinham coordenada viram MANUAL, para nenhuma importação sobrescrevê-las
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

-- 3) Alocação de estagiários (trocar de unidade encerra a alocação e cria outra)
CREATE TABLE IF NOT EXISTS "AlocacoesEstagiarios" (
    "IdAlocacao"   UUID        PRIMARY KEY,
    "IdUnidade"    UUID        NOT NULL,
    "IdEstagiario" UUID        NOT NULL,
    "DataInicio"   DATE        NOT NULL,
    "DataFim"      DATE        NULL,
    "Ativo"        BOOLEAN     NOT NULL DEFAULT TRUE,
    "Observacao"   TEXT        NULL,
    "CriadoPorId"  UUID        NULL,
    "CriadoEm"     TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo'),
    "AtualizadoEm" TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo'),

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

-- 4) Cache de geocodificação (inclusive os "não encontrado")
CREATE TABLE IF NOT EXISTS "GeocodificacaoCache" (
    "Id"                  UUID              PRIMARY KEY,
    "EnderecoNormalizado" VARCHAR(500)      NOT NULL,
    "Latitude"            DOUBLE PRECISION  NULL,
    "Longitude"           DOUBLE PRECISION  NULL,
    "EnderecoRetornado"   TEXT              NULL,
    "Precisao"            VARCHAR(100)      NULL,
    "Status"              VARCHAR(30)       NOT NULL DEFAULT 'pendente',
    "Provedor"            VARCHAR(30)       NOT NULL DEFAULT 'NOMINATIM',
    "CriadoEm"            TIMESTAMP         NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo'),
    "AtualizadoEm"        TIMESTAMP         NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo')
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_GeocodificacaoCache_Endereco"
    ON "GeocodificacaoCache" ("EnderecoNormalizado");

-- 5) Conferência
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

INSERT INTO "MigracoesAplicadas" ("Nome") VALUES ('007_unidades_saude')
ON CONFLICT ("Nome") DO NOTHING;

COMMIT;
