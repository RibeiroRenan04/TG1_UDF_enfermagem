-- 003 – renomeia tabelas e colunas para português, preservando os dados.
-- Só roda enquanto "Users" existir: o passo 1 apaga "Usuarios", "Locais" etc. com CASCADE, e
-- rodá-lo num banco já renomeado apagaria as tabelas reais.

BEGIN;

CREATE TABLE IF NOT EXISTS "MigracoesAplicadas" (
    "Nome"       TEXT        PRIMARY KEY,
    "AplicadaEm" TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo')
);

DO $migracao$
BEGIN
    IF to_regclass('public."Users"') IS NULL THEN
        RAISE NOTICE '003: tabela "Users" não existe (schema já em português) — nada a fazer.';
        RETURN;
    END IF;

    -- 1) Dropar tabelas duplicadas em português (vazias/incompletas) e snake_case
    DROP TABLE IF EXISTS "AcompanhamentosFormativos" CASCADE;
    DROP TABLE IF EXISTS "Avaliacoes" CASCADE;
    DROP TABLE IF EXISTS "RegistrosPresenca" CASCADE;
    DROP TABLE IF EXISTS "EscalasRodizio" CASCADE;
    DROP TABLE IF EXISTS "MembrosGrupo" CASCADE;
    DROP TABLE IF EXISTS "GruposEstudantes" CASCADE;
    DROP TABLE IF EXISTS "Locais" CASCADE;
    DROP TABLE IF EXISTS "Usuarios" CASCADE;
    DROP TABLE IF EXISTS password_reset_codes CASCADE;
    DROP TABLE IF EXISTS student_semester_history CASCADE;

    -- 2) Remover coluna RGM duplicada (mantém "Rgm")
    ALTER TABLE "Users" DROP COLUMN IF EXISTS "RGM";

    -- 3) Users -> Usuarios
    ALTER TABLE "Users" RENAME TO "Usuarios";
    ALTER TABLE "Usuarios" RENAME COLUMN "Id"                 TO "IdUsuario";
    ALTER TABLE "Usuarios" RENAME COLUMN "FullName"           TO "NomeCompleto";
    ALTER TABLE "Usuarios" RENAME COLUMN "PasswordHash"       TO "SenhaHash";
    ALTER TABLE "Usuarios" RENAME COLUMN "Role"               TO "Papel";
    ALTER TABLE "Usuarios" RENAME COLUMN "Phone"              TO "Telefone";
    ALTER TABLE "Usuarios" RENAME COLUMN "CreatedAt"          TO "CriadoEm";
    ALTER TABLE "Usuarios" RENAME COLUMN "UpdatedAt"          TO "AtualizadoEm";
    ALTER TABLE "Usuarios" RENAME COLUMN "Semester"           TO "Semestre";
    ALTER TABLE "Usuarios" RENAME COLUMN "Shift"              TO "Turno";
    ALTER TABLE "Usuarios" RENAME COLUMN "Institution"        TO "Instituicao";
    ALTER TABLE "Usuarios" RENAME COLUMN "MustChangePassword" TO "DeveTrocarSenha";
    ALTER TABLE "Usuarios" RENAME COLUMN "MustSetEmail"       TO "DeveDefinirEmail";
    ALTER TABLE "Usuarios" RENAME COLUMN "IsActive"           TO "Ativo";

    -- 4) Locations -> Locais
    ALTER TABLE "Locations" RENAME TO "Locais";
    ALTER TABLE "Locais" RENAME COLUMN "Id"            TO "IdLocal";
    ALTER TABLE "Locais" RENAME COLUMN "Name"          TO "Nome";
    ALTER TABLE "Locais" RENAME COLUMN "Address"       TO "Endereco";
    ALTER TABLE "Locais" RENAME COLUMN "RadiusMeters"  TO "RaioMetros";
    ALTER TABLE "Locais" RENAME COLUMN "IsInstitution" TO "EhInstituicao";
    ALTER TABLE "Locais" RENAME COLUMN "ShiftStart"    TO "InicioTurno";
    ALTER TABLE "Locais" RENAME COLUMN "ShiftEnd"      TO "FimTurno";
    ALTER TABLE "Locais" RENAME COLUMN "CreatedAt"     TO "CriadoEm";

    -- 5) StudentGroups -> GruposEstudantes
    ALTER TABLE "StudentGroups" RENAME TO "GruposEstudantes";
    ALTER TABLE "GruposEstudantes" RENAME COLUMN "Id"          TO "IdGrupo";
    ALTER TABLE "GruposEstudantes" RENAME COLUMN "Code"        TO "Codigo";
    ALTER TABLE "GruposEstudantes" RENAME COLUMN "Name"        TO "Nome";
    ALTER TABLE "GruposEstudantes" RENAME COLUMN "Description" TO "Descricao";
    ALTER TABLE "GruposEstudantes" RENAME COLUMN "CreatedAt"   TO "CriadoEm";

    -- 6) GroupMemberships -> MembrosGrupo
    ALTER TABLE "GroupMemberships" RENAME TO "MembrosGrupo";
    ALTER TABLE "MembrosGrupo" RENAME COLUMN "Id"        TO "IdMembroGrupo";
    ALTER TABLE "MembrosGrupo" RENAME COLUMN "StudentId" TO "IdEstudante";
    ALTER TABLE "MembrosGrupo" RENAME COLUMN "GroupId"   TO "IdGrupo";
    ALTER TABLE "MembrosGrupo" RENAME COLUMN "CreatedAt" TO "CriadoEm";

    -- 7) RotationSchedules -> EscalasRodizio
    ALTER TABLE "RotationSchedules" RENAME TO "EscalasRodizio";
    ALTER TABLE "EscalasRodizio" RENAME COLUMN "Id"            TO "IdEscala";
    ALTER TABLE "EscalasRodizio" RENAME COLUMN "GroupId"       TO "IdGrupo";
    ALTER TABLE "EscalasRodizio" RENAME COLUMN "LocationId"    TO "IdLocal";
    ALTER TABLE "EscalasRodizio" RENAME COLUMN "PreceptorId"   TO "IdPreceptor";
    ALTER TABLE "EscalasRodizio" RENAME COLUMN "Shift"         TO "Turno";
    ALTER TABLE "EscalasRodizio" RENAME COLUMN "PeriodLabel"   TO "RotuloPeriodo";
    ALTER TABLE "EscalasRodizio" RENAME COLUMN "StartDate"     TO "DataInicio";
    ALTER TABLE "EscalasRodizio" RENAME COLUMN "EndDate"       TO "DataFim";
    ALTER TABLE "EscalasRodizio" RENAME COLUMN "ActivityType"  TO "TipoAtividade";
    ALTER TABLE "EscalasRodizio" RENAME COLUMN "RequiredHours" TO "HorasExigidas";
    ALTER TABLE "EscalasRodizio" RENAME COLUMN "Notes"         TO "Observacoes";
    ALTER TABLE "EscalasRodizio" RENAME COLUMN "CreatedAt"     TO "CriadoEm";

    -- 8) AttendanceRecords -> RegistrosPresenca
    ALTER TABLE "AttendanceRecords" RENAME TO "RegistrosPresenca";
    ALTER TABLE "RegistrosPresenca" RENAME COLUMN "Id"                    TO "IdPresenca";
    ALTER TABLE "RegistrosPresenca" RENAME COLUMN "StudentId"             TO "IdEstudante";
    ALTER TABLE "RegistrosPresenca" RENAME COLUMN "ScheduleId"            TO "IdEscala";
    ALTER TABLE "RegistrosPresenca" RENAME COLUMN "LocationId"            TO "IdLocal";
    ALTER TABLE "RegistrosPresenca" RENAME COLUMN "Type"                  TO "Tipo";
    ALTER TABLE "RegistrosPresenca" RENAME COLUMN "RecordedAt"            TO "RegistradoEm";
    ALTER TABLE "RegistrosPresenca" RENAME COLUMN "DistanceMeters"        TO "DistanciaMetros";
    ALTER TABLE "RegistrosPresenca" RENAME COLUMN "PhotoUrl"              TO "UrlFoto";
    ALTER TABLE "RegistrosPresenca" RENAME COLUMN "ActivitiesDescription" TO "DescricaoAtividades";
    ALTER TABLE "RegistrosPresenca" RENAME COLUMN "IrregularityReason"    TO "MotivoIrregularidade";
    ALTER TABLE "RegistrosPresenca" RENAME COLUMN "ValidatedById"         TO "ValidadoPorId";
    ALTER TABLE "RegistrosPresenca" RENAME COLUMN "ValidatedAt"           TO "ValidadoEm";
    ALTER TABLE "RegistrosPresenca" RENAME COLUMN "CreatedAt"             TO "CriadoEm";

    -- 9) Evaluations -> Avaliacoes
    ALTER TABLE "Evaluations" RENAME TO "Avaliacoes";
    ALTER TABLE "Avaliacoes" RENAME COLUMN "Id"              TO "IdAvaliacao";
    ALTER TABLE "Avaliacoes" RENAME COLUMN "StudentId"       TO "IdEstudante";
    ALTER TABLE "Avaliacoes" RENAME COLUMN "PreceptorId"     TO "IdPreceptor";
    ALTER TABLE "Avaliacoes" RENAME COLUMN "ScheduleId"      TO "IdEscala";
    ALTER TABLE "Avaliacoes" RENAME COLUMN "ActivitiesScore" TO "NotaAtividades";
    ALTER TABLE "Avaliacoes" RENAME COLUMN "PostureScore"    TO "NotaPostura";
    ALTER TABLE "Avaliacoes" RENAME COLUMN "PlanningScore"   TO "NotaPlanejamento";
    ALTER TABLE "Avaliacoes" RENAME COLUMN "Comment"         TO "Comentario";
    ALTER TABLE "Avaliacoes" RENAME COLUMN "CreatedAt"       TO "CriadoEm";

    -- 10) FormativeFollowups -> AcompanhamentosFormativos
    ALTER TABLE "FormativeFollowups" RENAME TO "AcompanhamentosFormativos";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "Id"                  TO "IdAcompanhamento";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "StudentId"           TO "IdEstudante";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "PreceptorId"         TO "IdPreceptor";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "ScheduleId"          TO "IdEscala";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "GroupId"             TO "IdGrupo";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "LocationId"          TO "IdLocal";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "Shift"               TO "Turno";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "PeriodLabel"         TO "RotuloPeriodo";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "Semester"            TO "Semestre";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "FollowUpStart"       TO "InicioAcompanhamento";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "FollowUpEnd"         TO "FimAcompanhamento";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "PreceptorSignedAt"     TO "AssinadoPreceptorEm";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "PreceptorSignedName"   TO "NomeAssinaturaPreceptor";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "PreceptorSignedIp"     TO "IpAssinaturaPreceptor";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "PreceptorSignedUserId" TO "IdUsuarioAssinaturaPreceptor";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "StudentSignedAt"       TO "AssinadoEstudanteEm";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "StudentSignedName"     TO "NomeAssinaturaEstudante";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "StudentSignedIp"       TO "IpAssinaturaEstudante";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "StudentSignedUserId"   TO "IdUsuarioAssinaturaEstudante";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "CreatedAt"           TO "CriadoEm";
    ALTER TABLE "AcompanhamentosFormativos" RENAME COLUMN "UpdatedAt"           TO "AtualizadoEm";

    -- 11) PasswordResetCodes -> CodigosRedefinicaoSenha
    ALTER TABLE "PasswordResetCodes" RENAME TO "CodigosRedefinicaoSenha";
    ALTER TABLE "CodigosRedefinicaoSenha" RENAME COLUMN "Code"      TO "Codigo";
    ALTER TABLE "CodigosRedefinicaoSenha" RENAME COLUMN "ExpiresAt" TO "ExpiraEm";
    ALTER TABLE "CodigosRedefinicaoSenha" RENAME COLUMN "Used"      TO "Usado";
    ALTER TABLE "CodigosRedefinicaoSenha" RENAME COLUMN "CreatedAt" TO "CriadoEm";

    -- 12) StudentSemesterHistories -> HistoricoSemestreEstudante
    ALTER TABLE "StudentSemesterHistories" RENAME TO "HistoricoSemestreEstudante";
    ALTER TABLE "HistoricoSemestreEstudante" RENAME COLUMN "StudentId"  TO "IdEstudante";
    ALTER TABLE "HistoricoSemestreEstudante" RENAME COLUMN "Semester"   TO "Semestre";
    ALTER TABLE "HistoricoSemestreEstudante" RENAME COLUMN "TotalHours" TO "TotalHoras";
    ALTER TABLE "HistoricoSemestreEstudante" RENAME COLUMN "RecordedAt" TO "RegistradoEm";
END $migracao$;

INSERT INTO "MigracoesAplicadas" ("Nome") VALUES ('003_rename_to_portuguese')
ON CONFLICT ("Nome") DO NOTHING;

COMMIT;
