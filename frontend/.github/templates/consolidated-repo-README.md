# EstagioCheck — Repositório Consolidado

> ⚠️ **ATENÇÃO: Este repositório é somente leitura.**
>
> Não faça commits diretamente aqui.  
> Todas as alterações devem ser feitas nos repositórios originais:
> - **Frontend:** `RibeiroRenan04/EstagioCheckFront`
> - **Backend:** `RibeiroRenan04/EstagioCheckBack`
>
> Este repositório é atualizado automaticamente via GitHub Actions.

---

## Estrutura

```
EstagioCheck/
├── frontend/       ← sincronizado automaticamente de EstagioCheckFront
│   ├── frontend/   (app Angular)
│   ├── database/   (scripts SQL)
│   └── ...
├── backend/        ← sincronizado automaticamente de EstagioCheckBack
│   └── ...
└── README.md
```

## Repositórios Originais

| Repositório | Finalidade | Sincroniza para |
|---|---|---|
| `EstagioCheckFront` | Frontend Angular + Database | `/frontend` |
| `EstagioCheckBack`  | Backend / API               | `/backend`  |

## Como funciona

Cada repositório original possui um workflow `sync-to-consolidated.yml` que:
1. É disparado a cada `push` na branch `main`
2. Copia os arquivos via `rsync` para a pasta correspondente neste repo
3. Exclui automaticamente arquivos removidos na fonte (`--delete`)
4. Ignora artefatos de build, `node_modules`, `.env`, logs, etc.
5. Commita e faz push com a mensagem `[sync-bot]` para evitar loops
