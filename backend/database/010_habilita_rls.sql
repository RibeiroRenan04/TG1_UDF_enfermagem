-- 010 – liga RLS em todas as tabelas. O Supabase expõe o schema "public" pela API REST; sem RLS,
-- quem tem a publishable key lê e escreve tudo. A API conecta direto como "postgres" (ignora RLS),
-- e nenhuma política é criada de propósito: o único acesso legítimo é o do backend.

BEGIN;

CREATE TABLE IF NOT EXISTS "MigracoesAplicadas" (
    "Nome"       TEXT        PRIMARY KEY,
    "AplicadaEm" TIMESTAMP   NOT NULL DEFAULT (NOW() AT TIME ZONE 'America/Sao_Paulo')
);

ALTER TABLE "public"."__EFMigrationsHistory"        ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."Usuarios"                     ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."Locais"                       ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."GruposEstudantes"             ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."MembrosGrupo"                 ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."EscalasRodizio"               ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."RegistrosPresenca"            ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."Avaliacoes"                   ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."AcompanhamentosFormativos"    ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."CodigosRedefinicaoSenha"      ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."HistoricoSemestreEstudante"   ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."Irregularidades"              ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."MigracoesAplicadas"           ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."AlocacoesEstagiarios"         ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."GeocodificacaoCache"          ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."DiasRodizio"                  ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."AtividadesRemotas"            ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."ParticipacoesAtividadeRemota" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "public"."ExcecoesCalendario"           ENABLE ROW LEVEL SECURITY;

INSERT INTO "MigracoesAplicadas" ("Nome") VALUES ('010_habilita_rls')
ON CONFLICT ("Nome") DO NOTHING;

COMMIT;
