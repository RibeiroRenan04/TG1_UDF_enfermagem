#!/usr/bin/env bash
#
# Backup lógico do banco do EstágioCheck (ISO 22301 — ver docs/CONTINUIDADE_BACKUP.md).
#
#   BACKUP_PASSPHRASE='frase-longa' ./backup.sh "postgresql://usuario:senha@host:5432/postgres" [pasta]
#
# Gera <pasta>/estagiocheck_AAAAMMDD_HHMMSS.dump (formato custom do pg_dump, restaurável
# por tabela) e o .sha256 correspondente. Com BACKUP_PASSPHRASE definida, o dump sai
# criptografado com GPG (AES-256) e o arquivo em claro é apagado: o backup contém dados
# pessoais de alunos e não pode ficar legível fora do banco (LGPD art. 46).
#
# RETENCAO_DIAS (padrão 30) apaga da pasta os backups mais antigos que isso.

set -euo pipefail

if [[ $# -lt 1 ]]; then
    echo "uso: $0 <connection-string> [pasta-destino]" >&2
    exit 2
fi

CONN="$1"
DESTINO="${2:-./backups}"
RETENCAO_DIAS="${RETENCAO_DIAS:-30}"

for cmd in pg_dump sha256sum; do
    command -v "$cmd" >/dev/null 2>&1 || { echo "erro: $cmd não encontrado no PATH." >&2; exit 1; }
done

mkdir -p "$DESTINO"
ARQUIVO="$DESTINO/estagiocheck_$(date -u +%Y%m%d_%H%M%S).dump"

echo "── Gerando dump…"
# --no-owner/--no-privileges: o dump restaura em outro projeto/usuário sem depender dos papéis do Supabase.
pg_dump "$CONN" --format=custom --compress=9 --no-owner --no-privileges \
    --schema=public --file "$ARQUIVO"

if [[ -n "${BACKUP_PASSPHRASE:-}" ]]; then
    command -v gpg >/dev/null 2>&1 || { echo "erro: gpg não encontrado (necessário para criptografar)." >&2; rm -f "$ARQUIVO"; exit 1; }
    echo "── Criptografando (AES-256)…"
    gpg --batch --yes --symmetric --cipher-algo AES256 \
        --passphrase-fd 0 --output "$ARQUIVO.gpg" "$ARQUIVO" <<< "$BACKUP_PASSPHRASE"
    rm -f "$ARQUIVO"
    ARQUIVO="$ARQUIVO.gpg"
else
    echo "aviso: BACKUP_PASSPHRASE não definida — o dump fica SEM criptografia." >&2
fi

sha256sum "$ARQUIVO" > "$ARQUIVO.sha256"

echo "── Retenção: removendo backups com mais de $RETENCAO_DIAS dias…"
find "$DESTINO" -maxdepth 1 -name 'estagiocheck_*' -mtime +"$RETENCAO_DIAS" -print -delete

echo
echo "✔ Backup: $ARQUIVO ($(du -h "$ARQUIVO" | cut -f1))"
