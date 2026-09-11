-- =============================================================================
--  Migration 010 – Row Level Security
--
--  Liga RLS em todas as tabelas, igualando o que já vale em produção.
--
--  POR QUE ISSO É PRECISO:
--  O Supabase expõe cada tabela do schema "public" pela API REST (PostgREST).
--  Sem RLS, qualquer um com a publishable key e a URL do projeto lê e escreve
--  todas as linhas — inclusive "Usuarios" e os registros de ponto.
--
--  POR QUE NÃO QUEBRA A APLICAÇÃO:
--  A API não usa o SDK do Supabase. Ela conecta direto no PostgreSQL via Npgsql
--  (ConnectionStrings__DefaultConnection), com o papel "postgres", que ignora RLS.
--  O acesso que o RLS fecha é só o da API REST, que a aplicação não utiliza.
--
--  POR QUE NÃO HÁ POLÍTICAS:
--  De propósito. RLS ligado e nenhuma política significa "ninguém acessa por
--  este caminho", que é exatamente o desejado: o único acesso legítimo é o do
--  backend, pela conexão direta. O linter do Supabase reporta isso como INFO
--  ("RLS Enabled No Policy"), e produção carrega o mesmo aviso.
--
--  Só crie políticas aqui se algum dia um cliente passar a falar com o Supabase
--  diretamente. Hoje nenhum fala.
-- =============================================================================

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
