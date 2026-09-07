-- =============================================================================
--  Migration 009 – Programação do dia, atividades remotas e exceções
--
--  Muda a pergunta que comanda o ponto. Até aqui o sistema perguntava "onde o
--  aluno está?"; agora pergunta "onde ele DEVERIA estar e o que DEVERIA fazer
--  naquele dia?" — e o ponto passa a ser consequência dessa programação.
--
--  • PROGRAMAÇÃO SEMANAL DO RODÍZIO ("DiasRodizio")
--    O rodízio deixa de ter um único local para todo o período. Cada dia da
--    semana ganha um modo (presencial, remoto ou sem atividade) e, quando
--    presencial, o seu local — "segunda a quinta na UBS, sexta na faculdade"
--    vira cadastro, não uma regra fixa no código. Um rodízio sem nenhuma linha
--    aqui continua valendo como antes: dia útil presencial no local principal.
--
--  • ATIVIDADES REMOTAS ("AtividadesRemotas" e "ParticipacoesAtividadeRemota")
--    Nos dias em que a turma fica em casa a presença não pode depender de
--    localização. O professor cria a atividade, o sistema gera o código
--    ("ENF-7K92") e o aluno registra a participação informando-o dentro da
--    janela. O índice único de participação é a trava de "um uso por aluno".
--
--  • EXCEÇÕES DO CALENDÁRIO ("ExcecoesCalendario")
--    Feriado, recesso, estágio cancelado, dia que virou remoto, troca de local,
--    atividade especial e reposição. A exceção é cadastrada UMA vez com a sua
--    abrangência (faculdade, curso, turma, rodízio ou aluno) e a programação a
--    aplica sozinha — não é preciso mexer aluno por aluno.
--
--  • PONTO DA ATIVIDADE REMOTA ("RegistrosPresenca"."IdAtividadeRemota")
--    O ponto remoto entra na MESMA tabela do presencial, sem local e sem
--    coordenadas. É o que faz a carga horária remota contar no mesmo cálculo de
--    horas, em vez de criar uma segunda fonte de presença que divergiria.
--
--  Compatível com PostgreSQL (Supabase / Railway). O script é idempotente:
--  pode ser executado mais de uma vez sem quebrar.
-- =============================================================================

BEGIN;

-- ─────────────────────────────────────────────────────────────────────────────
--  1) Curso do aluno — alcance das exceções de abrangência "curso"
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE "Usuarios"
    ADD COLUMN IF NOT EXISTS "Curso" VARCHAR(150) NULL;

-- ─────────────────────────────────────────────────────────────────────────────
--  2) Programação por dia da semana do rodízio
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "DiasRodizio" (
    "IdDiaRodizio" UUID         PRIMARY KEY,
    "IdEscala"     UUID         NOT NULL,
    "DiaSemana"    INTEGER      NOT NULL,
    "Modo"         VARCHAR(20)  NOT NULL DEFAULT 'presencial',
    "IdLocal"      UUID         NULL,
    "Observacoes"  TEXT         NULL,
    "CriadoEm"     TIMESTAMP    NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_DiasRodizio_Escala"
        FOREIGN KEY ("IdEscala") REFERENCES "EscalasRodizio" ("IdEscala") ON DELETE CASCADE,
    CONSTRAINT "FK_DiasRodizio_Local"
        FOREIGN KEY ("IdLocal") REFERENCES "Locais" ("IdLocal") ON DELETE RESTRICT
);

-- 0 = domingo … 6 = sábado, o mesmo número que o .NET usa em DayOfWeek.
ALTER TABLE "DiasRodizio" DROP CONSTRAINT IF EXISTS "CK_DiasRodizio_DiaSemana";
ALTER TABLE "DiasRodizio"
    ADD CONSTRAINT "CK_DiasRodizio_DiaSemana" CHECK ("DiaSemana" BETWEEN 0 AND 6);

ALTER TABLE "DiasRodizio" DROP CONSTRAINT IF EXISTS "CK_DiasRodizio_Modo";
ALTER TABLE "DiasRodizio"
    ADD CONSTRAINT "CK_DiasRodizio_Modo"
    CHECK ("Modo" IN ('presencial', 'remoto', 'sem_atividade'));

-- Um rodízio tem, no máximo, uma regra por dia da semana.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_DiasRodizio_EscalaDia"
    ON "DiasRodizio" ("IdEscala", "DiaSemana");

-- ─────────────────────────────────────────────────────────────────────────────
--  3) Atividades remotas
-- ─────────────────────────────────────────────────────────────────────────────
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
    "CriadoEm"          TIMESTAMP     NOT NULL DEFAULT NOW(),
    "AtualizadoEm"      TIMESTAMP     NOT NULL DEFAULT NOW(),
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

-- O código identifica a atividade no momento do registro: duas do mesmo dia não
-- podem disputar o mesmo código.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_AtividadesRemotas_CodigoData"
    ON "AtividadesRemotas" ("CodigoPresenca", "Data");
CREATE INDEX IF NOT EXISTS "IX_AtividadesRemotas_Data"  ON "AtividadesRemotas" ("Data");
CREATE INDEX IF NOT EXISTS "IX_AtividadesRemotas_Grupo" ON "AtividadesRemotas" ("IdGrupo");

CREATE TABLE IF NOT EXISTS "ParticipacoesAtividadeRemota" (
    "IdParticipacao"    UUID         PRIMARY KEY,
    "IdAtividadeRemota" UUID         NOT NULL,
    "IdEstudante"       UUID         NOT NULL,
    "RegistradoEm"      TIMESTAMP    NOT NULL DEFAULT NOW(),
    "CodigoInformado"   VARCHAR(20)  NOT NULL,
    "RespostaTarefa"    TEXT         NULL,
    "IdPresenca"        UUID         NULL,
    "CriadoEm"          TIMESTAMP    NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_Participacoes_Atividade"
        FOREIGN KEY ("IdAtividadeRemota")
        REFERENCES "AtividadesRemotas" ("IdAtividadeRemota") ON DELETE CASCADE,
    CONSTRAINT "FK_Participacoes_Estudante"
        FOREIGN KEY ("IdEstudante") REFERENCES "Usuarios" ("IdUsuario") ON DELETE CASCADE
);

-- O código vale uma única vez por aluno.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Participacoes_AtividadeAluno"
    ON "ParticipacoesAtividadeRemota" ("IdAtividadeRemota", "IdEstudante");

-- ─────────────────────────────────────────────────────────────────────────────
--  4) Ponto vindo de atividade remota
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE "RegistrosPresenca"
    ADD COLUMN IF NOT EXISTS "IdAtividadeRemota" UUID NULL;

ALTER TABLE "RegistrosPresenca" DROP CONSTRAINT IF EXISTS "FK_RegistrosPresenca_AtividadeRemota";
ALTER TABLE "RegistrosPresenca"
    ADD CONSTRAINT "FK_RegistrosPresenca_AtividadeRemota"
    FOREIGN KEY ("IdAtividadeRemota")
    REFERENCES "AtividadesRemotas" ("IdAtividadeRemota") ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS "IX_RegistrosPresenca_AtividadeRemota"
    ON "RegistrosPresenca" ("IdAtividadeRemota");

-- ─────────────────────────────────────────────────────────────────────────────
--  5) Exceções do calendário
-- ─────────────────────────────────────────────────────────────────────────────
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
    "CriadoEm"          TIMESTAMP    NOT NULL DEFAULT NOW(),
    "AtualizadoEm"      TIMESTAMP    NOT NULL DEFAULT NOW(),
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

ALTER TABLE "ExcecoesCalendario" DROP CONSTRAINT IF EXISTS "CK_Excecoes_Abrangencia";
ALTER TABLE "ExcecoesCalendario"
    ADD CONSTRAINT "CK_Excecoes_Abrangencia"
    CHECK ("Abrangencia" IN ('faculdade', 'curso', 'turma', 'rodizio', 'aluno'));

ALTER TABLE "ExcecoesCalendario" DROP CONSTRAINT IF EXISTS "CK_Excecoes_Periodo";
ALTER TABLE "ExcecoesCalendario"
    ADD CONSTRAINT "CK_Excecoes_Periodo" CHECK ("DataFim" >= "DataInicio");

-- Cada abrangência exige o seu alvo: sem essa trava, uma exceção de turma sem
-- turma alcançaria a faculdade inteira sem que ninguém percebesse.
ALTER TABLE "ExcecoesCalendario" DROP CONSTRAINT IF EXISTS "CK_Excecoes_Alvo";
ALTER TABLE "ExcecoesCalendario"
    ADD CONSTRAINT "CK_Excecoes_Alvo" CHECK (
        ("Abrangencia" = 'faculdade')
     OR ("Abrangencia" = 'curso'   AND "Curso"       IS NOT NULL)
     OR ("Abrangencia" = 'turma'   AND "IdGrupo"     IS NOT NULL)
     OR ("Abrangencia" = 'rodizio' AND "IdEscala"    IS NOT NULL)
     OR ("Abrangencia" = 'aluno'   AND "IdEstudante" IS NOT NULL)
    );

CREATE INDEX IF NOT EXISTS "IX_Excecoes_Periodo"
    ON "ExcecoesCalendario" ("DataInicio", "DataFim");
CREATE INDEX IF NOT EXISTS "IX_Excecoes_Abrangencia"
    ON "ExcecoesCalendario" ("Abrangencia");

COMMIT;
