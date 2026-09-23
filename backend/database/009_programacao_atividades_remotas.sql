-- 009 – programação semanal do rodízio, atividades remotas com código de presença, exceções do
-- calendário e ponto remoto na mesma tabela do presencial (para contar no mesmo cálculo de horas).

BEGIN;

CREATE TABLE IF NOT EXISTS "MigracoesAplicadas" (
    "Nome"       TEXT        PRIMARY KEY,
    "AplicadaEm" TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo')
);

-- 1) Curso do aluno (não volta se o 012 já o removeu)
DO $migracao$
BEGIN
    IF to_regclass('public."ExcecoesCalendario"') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_schema = 'public'
                         AND table_name   = 'ExcecoesCalendario'
                         AND column_name  = 'Curso') THEN
        RETURN;
    END IF;

    ALTER TABLE "Usuarios"
        ADD COLUMN IF NOT EXISTS "Curso" VARCHAR(150) NULL;
END $migracao$;

-- 2) Programação por dia da semana do rodízio
CREATE TABLE IF NOT EXISTS "DiasRodizio" (
    "IdDiaRodizio" UUID         PRIMARY KEY,
    "IdEscala"     UUID         NOT NULL,
    "DiaSemana"    INTEGER      NOT NULL,
    "Modo"         VARCHAR(20)  NOT NULL DEFAULT 'presencial',
    "IdLocal"      UUID         NULL,
    "Observacoes"  TEXT         NULL,
    "CriadoEm"     TIMESTAMP    NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo'),
    CONSTRAINT "FK_DiasRodizio_Escala"
        FOREIGN KEY ("IdEscala") REFERENCES "EscalasRodizio" ("IdEscala") ON DELETE CASCADE,
    CONSTRAINT "FK_DiasRodizio_Local"
        FOREIGN KEY ("IdLocal") REFERENCES "Locais" ("IdLocal") ON DELETE RESTRICT
);

-- 0 = domingo … 6 = sábado, como DayOfWeek do .NET.
ALTER TABLE "DiasRodizio" DROP CONSTRAINT IF EXISTS "CK_DiasRodizio_DiaSemana";
ALTER TABLE "DiasRodizio"
    ADD CONSTRAINT "CK_DiasRodizio_DiaSemana" CHECK ("DiaSemana" BETWEEN 0 AND 6);

ALTER TABLE "DiasRodizio" DROP CONSTRAINT IF EXISTS "CK_DiasRodizio_Modo";
ALTER TABLE "DiasRodizio"
    ADD CONSTRAINT "CK_DiasRodizio_Modo"
    CHECK ("Modo" IN ('presencial', 'remoto', 'sem_atividade'));

CREATE UNIQUE INDEX IF NOT EXISTS "UX_DiasRodizio_EscalaDia"
    ON "DiasRodizio" ("IdEscala", "DiaSemana");

-- 3) Atividades remotas
CREATE TABLE IF NOT EXISTS "AtividadesRemotas" (
    "IdAtividadeRemota" UUID          PRIMARY KEY,
    "Titulo"            VARCHAR(200)  NOT NULL,
    "Descricao"         TEXT          NULL,
    "IdGrupo"           UUID          NOT NULL,
    "IdEscala"          UUID          NULL,
    "IdProfessor"       UUID          NOT NULL,
    "Data"              DATE          NOT NULL,
    "HoraInicio"        TIME          NOT NULL,
    "HoraFim"           TIME          NOT NULL,
    "CargaHoraria"      DOUBLE PRECISION NOT NULL DEFAULT 4,
    "CodigoPresenca"    VARCHAR(20)   NOT NULL,
    "ExigeTarefa"       BOOLEAN       NOT NULL DEFAULT FALSE,
    "TipoTarefa"        VARCHAR(30)   NULL,
    "InstrucoesTarefa"  TEXT          NULL,
    "Ativo"             BOOLEAN       NOT NULL DEFAULT TRUE,
    "CriadoEm"          TIMESTAMP     NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo'),
    "AtualizadoEm"      TIMESTAMP     NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo'),
    CONSTRAINT "FK_AtividadesRemotas_Grupo"
        FOREIGN KEY ("IdGrupo") REFERENCES "GruposEstudantes" ("IdGrupo") ON DELETE CASCADE,
    CONSTRAINT "FK_AtividadesRemotas_Escala"
        FOREIGN KEY ("IdEscala") REFERENCES "EscalasRodizio" ("IdEscala") ON DELETE SET NULL,
    CONSTRAINT "FK_AtividadesRemotas_Professor"
        FOREIGN KEY ("IdProfessor") REFERENCES "Usuarios" ("IdUsuario") ON DELETE RESTRICT
);

ALTER TABLE "AtividadesRemotas" DROP CONSTRAINT IF EXISTS "CK_AtividadesRemotas_Janela";
ALTER TABLE "AtividadesRemotas"
    ADD CONSTRAINT "CK_AtividadesRemotas_Janela" CHECK ("HoraFim" > "HoraInicio");

ALTER TABLE "AtividadesRemotas" DROP CONSTRAINT IF EXISTS "CK_AtividadesRemotas_TipoTarefa";
ALTER TABLE "AtividadesRemotas"
    ADD CONSTRAINT "CK_AtividadesRemotas_TipoTarefa"
    CHECK ("TipoTarefa" IS NULL OR "TipoTarefa" IN
        ('questionario', 'arquivo', 'discursiva', 'estudo_de_caso',
         'aula_online', 'leitura', 'formulario'));

-- Duas atividades do mesmo dia não podem disputar o mesmo código.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_AtividadesRemotas_CodigoData"
    ON "AtividadesRemotas" ("CodigoPresenca", "Data");
CREATE INDEX IF NOT EXISTS "IX_AtividadesRemotas_Data"  ON "AtividadesRemotas" ("Data");
CREATE INDEX IF NOT EXISTS "IX_AtividadesRemotas_Grupo" ON "AtividadesRemotas" ("IdGrupo");

CREATE TABLE IF NOT EXISTS "ParticipacoesAtividadeRemota" (
    "IdParticipacao"    UUID         PRIMARY KEY,
    "IdAtividadeRemota" UUID         NOT NULL,
    "IdEstudante"       UUID         NOT NULL,
    "RegistradoEm"      TIMESTAMP    NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo'),
    "CodigoInformado"   VARCHAR(20)  NOT NULL,
    "RespostaTarefa"    TEXT         NULL,
    "IdPresenca"        UUID         NULL,
    "CriadoEm"          TIMESTAMP    NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo'),
    CONSTRAINT "FK_Participacoes_Atividade"
        FOREIGN KEY ("IdAtividadeRemota")
        REFERENCES "AtividadesRemotas" ("IdAtividadeRemota") ON DELETE CASCADE,
    CONSTRAINT "FK_Participacoes_Estudante"
        FOREIGN KEY ("IdEstudante") REFERENCES "Usuarios" ("IdUsuario") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_Participacoes_AtividadeAluno"
    ON "ParticipacoesAtividadeRemota" ("IdAtividadeRemota", "IdEstudante");

-- 4) Ponto vindo de atividade remota
ALTER TABLE "RegistrosPresenca"
    ADD COLUMN IF NOT EXISTS "IdAtividadeRemota" UUID NULL;

ALTER TABLE "RegistrosPresenca" DROP CONSTRAINT IF EXISTS "FK_RegistrosPresenca_AtividadeRemota";
ALTER TABLE "RegistrosPresenca"
    ADD CONSTRAINT "FK_RegistrosPresenca_AtividadeRemota"
    FOREIGN KEY ("IdAtividadeRemota")
    REFERENCES "AtividadesRemotas" ("IdAtividadeRemota") ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS "IX_RegistrosPresenca_AtividadeRemota"
    ON "RegistrosPresenca" ("IdAtividadeRemota");

-- 5) Exceções do calendário
CREATE TABLE IF NOT EXISTS "ExcecoesCalendario" (
    "IdExcecao"         UUID         PRIMARY KEY,
    "Tipo"              VARCHAR(30)  NOT NULL,
    "Abrangencia"       VARCHAR(20)  NOT NULL,
    "DataInicio"        DATE         NOT NULL,
    "DataFim"           DATE         NOT NULL,
    "Turno"             VARCHAR(10)  NULL,
    "IdGrupo"           UUID         NULL,
    "IdEscala"          UUID         NULL,
    "IdEstudante"       UUID         NULL,
    "Curso"             VARCHAR(150) NULL,
    "IdLocal"           UUID         NULL,
    "IdAtividadeRemota" UUID         NULL,
    "Descricao"         VARCHAR(300) NOT NULL DEFAULT '',
    "CriadoPorId"       UUID         NULL,
    "CriadoEm"          TIMESTAMP    NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo'),
    "AtualizadoEm"      TIMESTAMP    NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo'),
    CONSTRAINT "FK_Excecoes_Grupo"
        FOREIGN KEY ("IdGrupo") REFERENCES "GruposEstudantes" ("IdGrupo") ON DELETE CASCADE,
    CONSTRAINT "FK_Excecoes_Escala"
        FOREIGN KEY ("IdEscala") REFERENCES "EscalasRodizio" ("IdEscala") ON DELETE CASCADE,
    CONSTRAINT "FK_Excecoes_Estudante"
        FOREIGN KEY ("IdEstudante") REFERENCES "Usuarios" ("IdUsuario") ON DELETE CASCADE,
    CONSTRAINT "FK_Excecoes_Local"
        FOREIGN KEY ("IdLocal") REFERENCES "Locais" ("IdLocal") ON DELETE RESTRICT,
    CONSTRAINT "FK_Excecoes_AtividadeRemota"
        FOREIGN KEY ("IdAtividadeRemota")
        REFERENCES "AtividadesRemotas" ("IdAtividadeRemota") ON DELETE SET NULL,
    CONSTRAINT "FK_Excecoes_CriadoPor"
        FOREIGN KEY ("CriadoPorId") REFERENCES "Usuarios" ("IdUsuario") ON DELETE SET NULL
);

ALTER TABLE "ExcecoesCalendario" DROP CONSTRAINT IF EXISTS "CK_Excecoes_Tipo";
ALTER TABLE "ExcecoesCalendario"
    ADD CONSTRAINT "CK_Excecoes_Tipo"
    CHECK ("Tipo" IN ('feriado', 'recesso', 'cancelado', 'remoto',
                      'troca_local', 'atividade_especial', 'reposicao'));

ALTER TABLE "ExcecoesCalendario" DROP CONSTRAINT IF EXISTS "CK_Excecoes_Periodo";
ALTER TABLE "ExcecoesCalendario"
    ADD CONSTRAINT "CK_Excecoes_Periodo" CHECK ("DataFim" >= "DataInicio");

-- Abrangência e alvo só são criadas se faltarem: o 012 as substitui, e recriá-las numa
-- reexecução traria "curso" de volta (e a CK_Excecoes_Alvo nem compilaria sem a coluna).
DO $migracao$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conname  = 'CK_Excecoes_Abrangencia'
                     AND conrelid = 'public."ExcecoesCalendario"'::regclass) THEN
        ALTER TABLE "ExcecoesCalendario"
            ADD CONSTRAINT "CK_Excecoes_Abrangencia"
            CHECK ("Abrangencia" IN ('faculdade', 'curso', 'turma', 'rodizio', 'aluno'));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conname  = 'CK_Excecoes_Alvo'
                     AND conrelid = 'public."ExcecoesCalendario"'::regclass) THEN
        ALTER TABLE "ExcecoesCalendario"
            ADD CONSTRAINT "CK_Excecoes_Alvo" CHECK (
                ("Abrangencia" = 'faculdade')
             OR ("Abrangencia" = 'curso'   AND "Curso"       IS NOT NULL)
             OR ("Abrangencia" = 'turma'   AND "IdGrupo"     IS NOT NULL)
             OR ("Abrangencia" = 'rodizio' AND "IdEscala"    IS NOT NULL)
             OR ("Abrangencia" = 'aluno'   AND "IdEstudante" IS NOT NULL)
            );
    END IF;
END $migracao$;

CREATE INDEX IF NOT EXISTS "IX_Excecoes_Periodo"
    ON "ExcecoesCalendario" ("DataInicio", "DataFim");
CREATE INDEX IF NOT EXISTS "IX_Excecoes_Abrangencia"
    ON "ExcecoesCalendario" ("Abrangencia");

INSERT INTO "MigracoesAplicadas" ("Nome") VALUES ('009_programacao_atividades_remotas')
ON CONFLICT ("Nome") DO NOTHING;

COMMIT;
