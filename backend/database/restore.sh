#!/usr/bin/env bash
#
# Restaura um backup gerado pelo backup.sh (ver docs/CONTINUIDADE_BACKUP.md).
#
#   BACKUP_PASSPHRASE='frase-longa' ./restore.sh <arquivo.dump[.gpg]> "postgresql://…/postgres"
#
# Confere o .sha256, descriptografa se preciso e restaura com pg_restore --clean, ou seja,
# SUBSTITUI os objetos existentes no banco de destino. Teste sempre num banco descartável
# primeiro (o teste trimestral de restauração faz exatamente isso).

set -euo pipefail

if [[ $# -ne 2 ]]; then
    echo "uso: $0 <arquivo-de-backup> <connection-string-destino>" >&2
    exit 2
fi

ARQUIVO="$1"
CONN="$2"

command -v pg_restore >/dev/null 2>&1 || { echo "erro: pg_restore não encontrado no PATH." >&2; exit 1; }
[[ -f "$ARQUIVO" ]] || { echo "erro: $ARQUIVO não existe." >&2; exit 1; }

if [[ -f "$ARQUIVO.sha256" ]]; then
    echo "── Conferindo integridade…"
    sha256sum --check --status "$ARQUIVO.sha256" || { echo "erro: checksum não confere — backup corrompido." >&2; exit 1; }
else
    echo "aviso: $ARQUIVO.sha256 não encontrado; integridade não verificada." >&2
fi

DUMP="$ARQUIVO"
TEMP=""
if [[ "$ARQUIVO" == *.gpg ]]; then
    [[ -n "${BACKUP_PASSPHRASE:-}" ]] || { echo "erro: defina BACKUP_PASSPHRASE para descriptografar." >&2; exit 1; }
    TEMP="$(mktemp)"
    trap 'rm -f "$TEMP"' EXIT
    echo "── Descriptografando…"
    gpg --batch --yes --decrypt --passphrase-fd 0 --output "$TEMP" "$ARQUIVO" <<< "$BACKUP_PASSPHRASE"
    DUMP="$TEMP"
fi

echo "Destino: ${CONN%%@*}@…"
read -r -p "Os dados do destino serão SUBSTITUÍDOS. Digite RESTAURAR para continuar: " resposta
[[ "$resposta" == "RESTAURAR" ]] || { echo "Cancelado."; exit 1; }

pg_restore --dbname "$CONN" --clean --if-exists --no-owner --no-privileges --exit-on-error "$DUMP"

echo
echo "✔ Restauração concluída. Confira com: SELECT \"Nome\" FROM \"MigracoesAplicadas\" ORDER BY 1;"
