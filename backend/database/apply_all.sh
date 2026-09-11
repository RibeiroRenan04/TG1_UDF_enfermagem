#!/usr/bin/env bash
#
# Aplica o schema completo do EstágioCheck em um banco PostgreSQL vazio.
#
#   ./apply_all.sh "postgresql://usuario:senha@host:5432/postgres"
#
# Executa migration.sql (schema base) e depois os scripts numerados, na ordem.
# Para no primeiro erro — um script que falha no meio deixa o banco pela metade,
# e seguir adiante só empilharia erros em cima do primeiro.
#
# Os scripts não são idempotentes (exceto o 005): use apenas em banco vazio.
# Para um banco onde a API já rodou, o schema base já existe — nesse caso pule
# o migration.sql e aplique só os numerados. Veja o README.md deste diretório.

set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "uso: $0 <connection-string>" >&2
    echo "exemplo: $0 \"postgresql://postgres:senha@db.exemplo.supabase.co:5432/postgres\"" >&2
    exit 2
fi

CONN="$1"
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v psql >/dev/null 2>&1; then
    echo "erro: psql não encontrado no PATH." >&2
    exit 1
fi

# migration.sql primeiro; depois os numerados em ordem lexicográfica, que
# coincide com a ordem cronológica por causa do prefixo de três dígitos.
SCRIPTS=("$DIR/migration.sql")
while IFS= read -r f; do
    SCRIPTS+=("$f")
done < <(find "$DIR" -maxdepth 1 -name '[0-9][0-9][0-9]_*.sql' | sort)

echo "Banco: ${CONN%%:*}://…  (${#SCRIPTS[@]} scripts)"
echo

for script in "${SCRIPTS[@]}"; do
    echo "── $(basename "$script")"
    # ON_ERROR_STOP faz o psql sair com código != 0 no primeiro erro;
    # sem isso ele seguiria executando os comandos seguintes do arquivo.
    psql "$CONN" --set ON_ERROR_STOP=on --quiet --file "$script"
done

echo
echo "✔ Schema aplicado."
