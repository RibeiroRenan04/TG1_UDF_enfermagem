# Banco de dados

Tudo que é preciso para levantar o banco do EstágioCheck do zero, na ordem certa.

## Como o schema nasce

Há **duas fontes** de mudança no banco, e a distinção importa:

| Origem | O que é | Como é aplicado |
|---|---|---|
| `backend/Migrations/` (EF Core) | O schema base. Fonte de verdade. | **Automático**: a API roda `db.Database.Migrate()` no startup (`backend/Program.cs`). |
| `database/002` … `013` | Evoluções posteriores. | **Manual**: executadas à mão, na ordem. Não são migrations do EF. |

Por isso `migration.sql` normalmente **não é necessário**: aponte a connection
string para um banco vazio, suba a API e o schema base se cria sozinho. O arquivo
existe para quem precisa criar o schema sem executar a aplicação.

## Ordem de execução

```
migration.sql   →  schema base (equivalente à migration 20260507011153_InitialPostgres)
002             →  colunas UDF em Usuários, códigos de redefinição de senha, CNES
003             →  renomeia tabelas e colunas para português
004             →  consolida Matrícula no RGM
005             →  irregularidades, permissão de atraso, perfil coordenadora
006             →  converte registros de ponto de UTC para horário de Brasília
007             →  módulo de Unidades de Saúde e geocodificação
008             →  alocação por turno e travas do ponto
009             →  programação do dia e atividades remotas
010             →  liga Row Level Security em todas as tabelas
011             →  permite o aluno em mais de uma turma (vínculo único por aluno + turma)
012             →  remove o curso do aluno e a abrangência "curso" das exceções
013             →  todos os horários do sistema em Brasília (dados, tipos e defaults)
```

A ordem não é negociável: `003` renomeia o que `migration.sql` e `002` criaram, e
tudo a partir dali assume os nomes em português.

## Provisionar um banco novo

### Opção A — deixar a API criar o schema base (recomendado)

1. Crie o banco vazio (ex.: projeto novo no Supabase).
2. Aponte `ConnectionStrings__DefaultConnection` para ele.
3. Suba a API. O EF Core cria o schema base e registra em `__EFMigrationsHistory`.
4. Aplique `002` … `013` na ordem (ou rode o `apply_all.sh`: o `migration.sql` percebe
   que o schema base já existe e é pulado).

### Opção B — tudo por SQL, sem executar a API

```bash
./apply_all.sh "postgresql://usuario:senha@host:5432/postgres"
```

O script executa `migration.sql` e depois `002` … `013`, parando no primeiro erro.

## Por que `migration.sql` registra a própria migration

O arquivo termina inserindo a linha correspondente em `__EFMigrationsHistory`.
Sem esse registro, a API tentaria aplicar `InitialPostgres` de novo no próximo
startup e falharia, porque as tabelas já existiriam. Se você criar o schema por
SQL, esse `INSERT` é o que mantém o EF Core em dia com a realidade do banco.

## Convenções do schema base

São as do EF Core / Npgsql, não escolhas destes arquivos:

- Identificadores entre aspas duplas preservam o PascalCase.
- `"Id"` é `uuid` **sem default** — o valor vem da aplicação, não do banco.
- Datas são `timestamp without time zone`, **no horário de Brasília (GMT-3)**. É a
  aplicação que grava o valor já no fuso local (`BrasiliaTime.Agora`); os defaults
  do banco usam `(NOW() AT TIME ZONE 'America/Sao_Paulo')` — nunca `NOW()` puro,
  que num servidor em UTC (Supabase, Railway) gravaria 3 horas adiantado.
- Chaves e índices seguem o padrão do EF (`PK_`, `FK_`, `IX_`). Os scripts
  seguintes dependem desses nomes: `002`, por exemplo, faz
  `DROP INDEX "IX_Users_Email"` para trocá-lo por um índice parcial.

## Idempotência

**Todos os scripts podem rodar mais de uma vez**, em qualquer banco PostgreSQL 13+
(Supabase, Railway, local). Cada um roda numa transação: ou aplica inteiro, ou nada muda.
Isso vale para provisionar um banco novo, completar um que parou no meio e reaplicar
por engano — o `apply_all.sh` pode ser executado de novo sem medo.

Como cada script garante isso:

| Script | Mecanismo |
|---|---|
| `migration.sql` | Pula tudo se `InitialPostgres` já está em `__EFMigrationsHistory`. |
| `002`, `003` | Só rodam enquanto a tabela `"Users"` existe (schema ainda em inglês). No `003` a trava é vital: ele apaga `"Usuarios"`, `"Locais"` etc. com `CASCADE` antes de renomear. |
| `004` | Só roda enquanto a coluna `"Matricula"` existe. |
| `005` | Estrutura repetível; remoção do "14" do RGM e backfill de irregularidades só na primeira execução. |
| `006`, `013` | Conversão UTC → Brasília (subtrai 3 h) protegida por `"MigracoesAplicadas"`. |
| `008` | O turno das alocações só é herdado do cadastro no momento em que a coluna nasce. |
| `009` | Não recria o curso nem as travas de abrangência que o `012` já substituiu. |
| demais | `IF NOT EXISTS` / `DROP … IF EXISTS` antes de recriar. |

### Controle de execução (`"MigracoesAplicadas"`)

Todo script numerado cria a tabela se ela não existir e, ao terminar, registra o
próprio nome nela. Para saber em que ponto está um banco:

```sql
SELECT "Nome", "AplicadaEm" FROM "MigracoesAplicadas" ORDER BY "Nome";
```

Bancos antigos só têm o registro do `006` (o controle nasceu nele); os outros
aparecem na próxima vez que os scripts forem reaplicados.

### Fuso horário: `006` e `013`

As duas únicas etapas **não** repetíveis por natureza: convertem dados gravados em
UTC para Brasília. Em banco novo não há dados e elas só se registram. Em banco com
dados, aplique cada uma **junto com o deploy do backend** correspondente — o `013`
acompanha a versão em que usuários, turmas, rodízios, avaliações, acompanhamentos e
códigos de senha passaram a ser gravados com `BrasiliaTime.Agora`. Um registro
gravado em Brasília antes do script rodar seria deslocado 3 h a mais.
