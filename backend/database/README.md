# Banco de dados

Tudo que é preciso para levantar o banco do EstágioCheck do zero, na ordem certa.

## Como o schema nasce

Há **duas fontes** de mudança no banco, e a distinção importa:

| Origem | O que é | Como é aplicado |
|---|---|---|
| `backend/Migrations/` (EF Core) | O schema base. Fonte de verdade. | **Automático**: a API roda `db.Database.Migrate()` no startup (`backend/Program.cs`). |
| `database/002` … `010` | Evoluções posteriores. | **Manual**: executadas à mão, na ordem. Não são migrations do EF. |

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
```

A ordem não é negociável: `003` renomeia o que `migration.sql` e `002` criaram, e
tudo a partir dali assume os nomes em português.

## Provisionar um banco novo

### Opção A — deixar a API criar o schema base (recomendado)

1. Crie o banco vazio (ex.: projeto novo no Supabase).
2. Aponte `ConnectionStrings__DefaultConnection` para ele.
3. Suba a API. O EF Core cria o schema base e registra em `__EFMigrationsHistory`.
4. Aplique `002` … `010` na ordem.

### Opção B — tudo por SQL, sem executar a API

```bash
./apply_all.sh "postgresql://usuario:senha@host:5432/postgres"
```

O script executa `migration.sql` e depois `002` … `010`, parando no primeiro erro.

## Por que `migration.sql` registra a própria migration

O arquivo termina inserindo a linha correspondente em `__EFMigrationsHistory`.
Sem esse registro, a API tentaria aplicar `InitialPostgres` de novo no próximo
startup e falharia, porque as tabelas já existiriam. Se você criar o schema por
SQL, esse `INSERT` é o que mantém o EF Core em dia com a realidade do banco.

## Convenções do schema base

São as do EF Core / Npgsql, não escolhas destes arquivos:

- Identificadores entre aspas duplas preservam o PascalCase.
- `"Id"` é `uuid` **sem default** — o valor vem da aplicação, não do banco.
- Datas são `timestamp without time zone`.
- Chaves e índices seguem o padrão do EF (`PK_`, `FK_`, `IX_`). Os scripts
  seguintes dependem desses nomes: `002`, por exemplo, faz
  `DROP INDEX "IX_Users_Email"` para trocá-lo por um índice parcial.

## Idempotência

`005`, `007`, `008` e `010` podem rodar mais de uma vez. As demais **não** são
idempotentes: rodar duas vezes causa erro (`003` tenta renomear tabelas que já
foram renomeadas, por exemplo). Aplique cada uma exatamente uma vez por banco.
