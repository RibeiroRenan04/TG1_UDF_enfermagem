-- =============================================================================
--  EstagioCheck – Schema base (PostgreSQL / Supabase)
--
--  Equivale à migration do EF Core "20260507011153_InitialPostgres", que é a
--  fonte de verdade do schema base (backend/Migrations/).
--
--  QUANDO USAR ESTE ARQUIVO:
--  Normalmente nunca. A API aplica as migrations do EF Core sozinha no startup
--  (Program.cs — db.Database.Migrate()), então basta apontar a connection string
--  para um banco vazio e subir a aplicação. Use este script apenas para criar o
--  schema sem executar a API (inspeção, provisionamento manual, banco de teste).
--
--  O script registra a migration em "__EFMigrationsHistory". Isso é essencial:
--  sem esse registro, a API tentaria aplicar "InitialPostgres" de novo no
--  próximo startup e falharia porque as tabelas já existem.
--
--  DEPOIS DESTE ARQUIVO, aplique na ordem os scripts numerados deste diretório
--  (002 … 009), que é como o banco de produção foi construído. Eles não são
--  migrations do EF Core — foram aplicados manualmente.
--
--  Convenções (as do EF Core / Npgsql, não escolhas deste arquivo):
--    • Identificadores entre aspas duplas preservam o PascalCase.
--    • "Id" é uuid SEM default: o valor é gerado pela aplicação, não pelo banco.
--    • Datas são "timestamp without time zone".
--    • Os nomes de chaves e índices seguem o padrão do EF (PK_, FK_, IX_),
--      dos quais os scripts seguintes dependem — 002, por exemplo, faz
--      DROP INDEX "IX_Users_Email".
-- =============================================================================

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    character varying(150) NOT NULL,
    "ProductVersion" character varying(32)  NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

CREATE TABLE "Locations" (
    "Id"            uuid                   NOT NULL,
    "Name"          character varying(300) NOT NULL,
    "Address"       character varying(500) NULL,
    "Latitude"      double precision       NOT NULL,
    "Longitude"     double precision       NOT NULL,
    "RadiusMeters"  integer                NOT NULL,
    "IsInstitution" boolean                NOT NULL,
    "ShiftStart"    character varying(5)   NOT NULL,
    "ShiftEnd"      character varying(5)   NOT NULL,
    "CreatedAt"     timestamp without time zone NOT NULL,
    CONSTRAINT "PK_Locations" PRIMARY KEY ("Id")
);

CREATE TABLE "StudentGroups" (
    "Id"          uuid                   NOT NULL,
    "Code"        character varying(20)  NOT NULL,
    "Name"        character varying(200) NOT NULL,
    "Description" text                   NULL,
    "CreatedAt"   timestamp without time zone NOT NULL,
    CONSTRAINT "PK_StudentGroups" PRIMARY KEY ("Id")
);

CREATE TABLE "Users" (
    "Id"           uuid                   NOT NULL,
    "FullName"     character varying(200) NOT NULL,
    "Email"        character varying(255) NOT NULL,
    "PasswordHash" text                   NOT NULL,
    "Role"         character varying(20)  NOT NULL DEFAULT 'aluno',
    "Matricula"    character varying(50)  NULL,
    "Phone"        character varying(30)  NULL,
    "CreatedAt"    timestamp without time zone NOT NULL,
    "UpdatedAt"    timestamp without time zone NOT NULL,
    CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
);

CREATE TABLE "GroupMemberships" (
    "Id"        uuid NOT NULL,
    "StudentId" uuid NOT NULL,
    "GroupId"   uuid NOT NULL,
    "CreatedAt" timestamp without time zone NOT NULL,
    CONSTRAINT "PK_GroupMemberships" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_GroupMemberships_StudentGroups_GroupId" FOREIGN KEY ("GroupId")
        REFERENCES "StudentGroups" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_GroupMemberships_Users_StudentId" FOREIGN KEY ("StudentId")
        REFERENCES "Users" ("Id") ON DELETE CASCADE
);

CREATE TABLE "RotationSchedules" (
    "Id"            uuid                   NOT NULL,
    "GroupId"       uuid                   NOT NULL,
    "LocationId"    uuid                   NOT NULL,
    "PreceptorId"   uuid                   NULL,
    "Shift"         character varying(10)  NOT NULL,
    "PeriodLabel"   character varying(100) NOT NULL,
    "StartDate"     date                   NOT NULL,
    "EndDate"       date                   NOT NULL,
    "ActivityType"  character varying(20)  NOT NULL,
    "RequiredHours" integer                NOT NULL,
    "Notes"         text                   NULL,
    "CreatedAt"     timestamp without time zone NOT NULL,
    CONSTRAINT "PK_RotationSchedules" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_RotationSchedules_Locations_LocationId" FOREIGN KEY ("LocationId")
        REFERENCES "Locations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_RotationSchedules_StudentGroups_GroupId" FOREIGN KEY ("GroupId")
        REFERENCES "StudentGroups" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_RotationSchedules_Users_PreceptorId" FOREIGN KEY ("PreceptorId")
        REFERENCES "Users" ("Id") ON DELETE SET NULL
);

CREATE TABLE "AttendanceRecords" (
    "Id"                    uuid                  NOT NULL,
    "StudentId"             uuid                  NOT NULL,
    "ScheduleId"            uuid                  NULL,
    "LocationId"            uuid                  NULL,
    "Type"                  character varying(10) NOT NULL,
    "RecordedAt"            timestamp without time zone NOT NULL,
    "Latitude"              double precision      NOT NULL,
    "Longitude"             double precision      NOT NULL,
    "DistanceMeters"        double precision      NULL,
    "PhotoUrl"              text                  NULL,
    "ActivitiesDescription" text                  NULL,
    "Status"                character varying(15) NOT NULL DEFAULT 'pendente',
    "IrregularityReason"    text                  NULL,
    "ValidatedById"         uuid                  NULL,
    "ValidatedAt"           timestamp without time zone NULL,
    "CreatedAt"             timestamp without time zone NOT NULL,
    CONSTRAINT "PK_AttendanceRecords" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AttendanceRecords_Locations_LocationId" FOREIGN KEY ("LocationId")
        REFERENCES "Locations" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_AttendanceRecords_RotationSchedules_ScheduleId" FOREIGN KEY ("ScheduleId")
        REFERENCES "RotationSchedules" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_AttendanceRecords_Users_StudentId" FOREIGN KEY ("StudentId")
        REFERENCES "Users" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AttendanceRecords_Users_ValidatedById" FOREIGN KEY ("ValidatedById")
        REFERENCES "Users" ("Id") ON DELETE SET NULL
);

CREATE TABLE "Evaluations" (
    "Id"              uuid     NOT NULL,
    "StudentId"       uuid     NOT NULL,
    "PreceptorId"     uuid     NOT NULL,
    "ScheduleId"      uuid     NULL,
    "ActivitiesScore" smallint NOT NULL,
    "PostureScore"    smallint NOT NULL,
    "PlanningScore"   smallint NOT NULL,
    "Comment"         text     NULL,
    "CreatedAt"       timestamp without time zone NOT NULL,
    CONSTRAINT "PK_Evaluations" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Evaluations_RotationSchedules_ScheduleId" FOREIGN KEY ("ScheduleId")
        REFERENCES "RotationSchedules" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Evaluations_Users_PreceptorId" FOREIGN KEY ("PreceptorId")
        REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Evaluations_Users_StudentId" FOREIGN KEY ("StudentId")
        REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "FormativeFollowups" (
    "Id"          uuid NOT NULL,
    "StudentId"   uuid NOT NULL,
    "PreceptorId" uuid NOT NULL,
    "ScheduleId"  uuid NULL,
    "GroupId"     uuid NULL,
    "LocationId"  uuid NULL,
    "Shift"       text NULL,
    "PeriodLabel" text NULL,
    "Semester"    text NULL,
    "FollowUpStart" date NULL,
    "FollowUpEnd"   date NULL,
    -- Postura profissional e ética
    "PosturaPontualidade"     text NULL,
    "PosturaEtica"            text NULL,
    "PosturaResponsabilidade" text NULL,
    -- Comunicação e trabalho em equipe
    "ComunicacaoEquipe"   text NULL,
    "ComunicacaoPaciente" text NULL,
    "ComunicacaoEscuta"   text NULL,
    -- Organização e segurança no cuidado
    "OrganizacaoPlanejamento" text NULL,
    "OrganizacaoSeguranca"    text NULL,
    "OrganizacaoRegistros"    text NULL,
    -- Participação e desenvolvimento
    "ParticipacaoIniciativa"  text NULL,
    "ParticipacaoAprendizado" text NULL,
    "ParticipacaoAutocritica" text NULL,
    -- Campos descritivos
    "Potencialidades"     text NULL,
    "AspectosAprimorar"   text NULL,
    "SituacoesRelevantes" text NULL,
    "ObservacoesDocente"  text NULL,
    "EvolucaoSemanal"     text NULL,
    -- Status
    "Status" character varying(30) NOT NULL DEFAULT 'rascunho',
    -- Assinatura do preceptor
    "PreceptorSignedAt"     timestamp without time zone NULL,
    "PreceptorSignedName"   text NULL,
    "PreceptorSignedIp"     text NULL,
    "PreceptorSignedUserId" uuid NULL,
    -- Assinatura do aluno
    "StudentSignedAt"     timestamp without time zone NULL,
    "StudentSignedName"   text NULL,
    "StudentSignedIp"     text NULL,
    "StudentSignedUserId" uuid NULL,
    "CreatedAt" timestamp without time zone NOT NULL,
    "UpdatedAt" timestamp without time zone NOT NULL,
    CONSTRAINT "PK_FormativeFollowups" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_FormativeFollowups_Locations_LocationId" FOREIGN KEY ("LocationId")
        REFERENCES "Locations" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_FormativeFollowups_RotationSchedules_ScheduleId" FOREIGN KEY ("ScheduleId")
        REFERENCES "RotationSchedules" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_FormativeFollowups_StudentGroups_GroupId" FOREIGN KEY ("GroupId")
        REFERENCES "StudentGroups" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_FormativeFollowups_Users_PreceptorId" FOREIGN KEY ("PreceptorId")
        REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_FormativeFollowups_Users_StudentId" FOREIGN KEY ("StudentId")
        REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

-- ── Índices ───────────────────────────────────────────────────────────────────
CREATE INDEX "IX_AttendanceRecords_LocationId"    ON "AttendanceRecords" ("LocationId");
CREATE INDEX "IX_AttendanceRecords_ScheduleId"    ON "AttendanceRecords" ("ScheduleId");
CREATE INDEX "IX_AttendanceRecords_StudentId"     ON "AttendanceRecords" ("StudentId");
CREATE INDEX "IX_AttendanceRecords_ValidatedById" ON "AttendanceRecords" ("ValidatedById");

CREATE INDEX "IX_Evaluations_PreceptorId" ON "Evaluations" ("PreceptorId");
CREATE INDEX "IX_Evaluations_ScheduleId"  ON "Evaluations" ("ScheduleId");
CREATE INDEX "IX_Evaluations_StudentId"   ON "Evaluations" ("StudentId");

CREATE INDEX "IX_FormativeFollowups_GroupId"     ON "FormativeFollowups" ("GroupId");
CREATE INDEX "IX_FormativeFollowups_LocationId"  ON "FormativeFollowups" ("LocationId");
CREATE INDEX "IX_FormativeFollowups_PreceptorId" ON "FormativeFollowups" ("PreceptorId");
CREATE INDEX "IX_FormativeFollowups_ScheduleId"  ON "FormativeFollowups" ("ScheduleId");
CREATE INDEX "IX_FormativeFollowups_StudentId"   ON "FormativeFollowups" ("StudentId");

CREATE INDEX "IX_GroupMemberships_GroupId" ON "GroupMemberships" ("GroupId");
CREATE UNIQUE INDEX "IX_GroupMemberships_StudentId" ON "GroupMemberships" ("StudentId");

CREATE INDEX "IX_RotationSchedules_GroupId"     ON "RotationSchedules" ("GroupId");
CREATE INDEX "IX_RotationSchedules_LocationId"  ON "RotationSchedules" ("LocationId");
CREATE INDEX "IX_RotationSchedules_PreceptorId" ON "RotationSchedules" ("PreceptorId");

CREATE UNIQUE INDEX "IX_StudentGroups_Code" ON "StudentGroups" ("Code");

CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");

-- ── Registro da migration ─────────────────────────────────────────────────────
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260507011153_InitialPostgres', '8.0.11');

COMMIT;
