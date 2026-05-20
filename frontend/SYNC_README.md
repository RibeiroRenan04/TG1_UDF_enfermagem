# Sistema de Sincronização Automática entre Repositórios
### GitHub Actions · Repositório Consolidado · EstagioCheck

---

## Índice

1. [Visão geral da arquitetura](#1-visão-geral-da-arquitetura)
2. [Repositórios envolvidos](#2-repositórios-envolvidos)
3. [Como gerar o PAT_TOKEN](#3-como-gerar-o-pat_token)
4. [Quais Secrets criar e onde](#4-quais-secrets-criar-e-onde)
5. [Permissões necessárias](#5-permissões-necessárias)
6. [Workflows criados](#6-workflows-criados)
7. [Onde colocar cada arquivo](#7-onde-colocar-cada-arquivo)
8. [Como inicializar o repositório consolidado](#8-como-inicializar-o-repositório-consolidado)
9. [Prevenção de loop infinito](#9-prevenção-de-loop-infinito)
10. [Arquivos ignorados](#10-arquivos-ignorados)
11. [Deleção de arquivos](#11-deleção-de-arquivos)
12. [Como testar](#12-como-testar)
13. [Erros comuns e soluções](#13-erros-comuns-e-soluções)
14. [Melhorias recomendadas](#14-melhorias-recomendadas)
15. [Estrutura final esperada](#15-estrutura-final-esperada)

---

## 1. Visão geral da arquitetura

```
┌─────────────────────────────────┐
│  EstagioCheckFront (frontend)   │
│  push → main                    │
│  ↓ GitHub Actions               │
│  .github/workflows/             │
│    sync-to-consolidated.yml     │
└─────────────────┬───────────────┘
                  │  rsync → /frontend
                  ▼
        ┌─────────────────────┐
        │   EstagioCheck      │
        │  (consolidado)      │
        │  ├── frontend/      │
        │  └── backend/       │
        └─────────────────────┘
                  ▲
                  │  rsync → /backend
┌─────────────────┴───────────────┐
│  EstagioCheckBack (backend)     │
│  push → main                    │
│  ↓ GitHub Actions               │
│  .github/workflows/             │
│    sync-to-consolidated.yml     │
└─────────────────────────────────┘
```

**Regras fundamentais:**
- O repo consolidado **nunca** tem workflows ativos
- Nenhum desenvolvedor faz commit direto no consolidado
- Todo push nos repos originais dispara a sincronização automaticamente
- Arquivos deletados na fonte são deletados no consolidado via `rsync --delete`

---

## 2. Repositórios envolvidos

| # | Repositório | Função | Branch | Workflow |
|---|---|---|---|---|
| 1 | `RibeiroRenan04/EstagioCheckFront` | Fonte — Frontend Angular + Database | `main` | ✅ Tem |
| 2 | `RibeiroRenan04/EstagioCheckAPI` | Fonte — Backend / API | `main` | ✅ Tem |
| 3 | `RibeiroRenan04/TG1_UDF_enfermagem` | **Consolidado — somente leitura** | `main` | ❌ Não tem |

> ⚠️ Substitua os nomes acima pelos nomes reais dos seus repositórios se forem diferentes.

---

## 3. Como gerar o PAT_TOKEN

O **Personal Access Token (PAT)** é a chave que permite que o GitHub Actions de um
repositório escreva em outro repositório (o consolidado).

### Passo a passo

1. Acesse: **GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)**
   - URL direta: https://github.com/settings/tokens

2. Clique em **"Generate new token (classic)"**

3. Configure:
   - **Note:** `sync-repos-pat` (nome descritivo)
   - **Expiration:** `No expiration` _(ou 1 ano, renovando anualmente)_
   - **Scopes selecionados:**
     - ☑️ `repo` → Full control of private repositories
       - ☑️ `repo:status`
       - ☑️ `repo_deployment`
       - ☑️ `public_repo`
       - ☑️ `repo:invite`
       - ☑️ `security_events`

4. Clique em **"Generate token"**

5. **Copie o token imediatamente** — ele não será exibido novamente.
   - Formato: `ghp_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx`

### Alternativa: Fine-grained token (mais seguro)

1. Acesse: **GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens**

2. Clique em **"Generate new token"**

3. Configure:
   - **Token name:** `sync-repos-pat`
   - **Expiration:** `90 days` (recomendado)
   - **Resource owner:** `RibeiroRenan04`
   - **Repository access:** `Only select repositories`
     - Selecione: `EstagioCheck` _(somente o consolidado)_
   - **Permissions → Repository permissions:**
     - `Contents` → `Read and write`
     - `Metadata` → `Read-only` _(obrigatório automaticamente)_

4. Gere e copie o token.

---

## 4. Quais Secrets criar e onde

O mesmo token precisa ser adicionado como secret em **cada repositório fonte**.

### No repositório `EstagioCheckFront`

1. Acesse: `github.com/RibeiroRenan04/EstagioCheckFront`
2. Vá em: **Settings → Secrets and variables → Actions**
3. Clique em **"New repository secret"**
4. Configure:
   - **Name:** `PAT_TOKEN`
   - **Secret:** _(cole o token gerado no passo anterior)_
5. Clique em **"Add secret"**

### No repositório `EstagioCheckBack`

Repita o mesmo processo acima no repositório de backend.

> **Resumo:** O secret `PAT_TOKEN` deve existir em:
> - ✅ `EstagioCheckFront` → Settings → Secrets → Actions
> - ✅ `EstagioCheckBack` → Settings → Secrets → Actions
> - ❌ `EstagioCheck` (consolidado) → **não precisa**

---

## 5. Permissões necessárias

### No repositório consolidado (`EstagioCheck`)

1. Acesse: `github.com/RibeiroRenan04/EstagioCheck`
2. Vá em: **Settings → Actions → General**
3. Em **"Workflow permissions"**, selecione:
   - ☑️ `Read and write permissions`
4. Clique em **"Save"**

### Nos repositórios fonte (frontend e backend)

1. Acesse cada repositório fonte
2. Vá em: **Settings → Actions → General**
3. Em **"Actions permissions"**:
   - ☑️ `Allow all actions and reusable workflows`
4. Em **"Workflow permissions"**:
   - ☑️ `Read repository contents and packages permissions` _(padrão é suficiente)_
5. Clique em **"Save"**

---

## 6. Workflows criados

### Workflow do Frontend

**Arquivo:** `.github/workflows/sync-to-consolidated.yml` (neste repositório)

| Propriedade | Valor |
|---|---|
| Trigger | `push` na branch `main` + `workflow_dispatch` |
| Runner | `ubuntu-latest` |
| Destino no consolidado | `/frontend` |
| Proteção anti-loop | `[sync-bot]` na mensagem de commit |
| Ferramenta de cópia | `rsync` com `--delete` |

### Workflow do Backend

**Arquivo:** `.github/workflows/sync-to-consolidated.yml` (no repositório backend)

Idêntico ao frontend, mas sincroniza para `/backend`.  
O template está em: `.github/templates/backend-sync-workflow.yml`

---

## 7. Onde colocar cada arquivo

```
EstagioCheckFront/                     ← este repositório
└── .github/
    ├── workflows/
    │   └── sync-to-consolidated.yml   ← ✅ JÁ CRIADO — workflow ativo do frontend
    └── templates/
        ├── backend-sync-workflow.yml  ← template para copiar ao backend
        └── consolidated-repo-README.md ← README para o repo consolidado

EstagioCheckBack/                      ← seu repositório de backend
└── .github/
    └── workflows/
        └── sync-to-consolidated.yml   ← copie o conteúdo de backend-sync-workflow.yml aqui

EstagioCheck/                          ← repositório consolidado (criar no GitHub)
├── frontend/                          ← preenchido automaticamente pela sync
├── backend/                           ← preenchido automaticamente pela sync
└── README.md                          ← copie o conteúdo de consolidated-repo-README.md aqui
```

---

## 8. Como inicializar o repositório consolidado

Execute os seguintes comandos no seu terminal local (Windows PowerShell ou Git Bash):

```bash
# 1. Crie o repositório no GitHub pelo site:
#    github.com/new → nome: EstagioCheck → Public ou Private → Create repository

# 2. Clone o novo repositório vazio
git clone https://github.com/RibeiroRenan04/EstagioCheck.git
cd EstagioCheck

# 3. Crie a estrutura inicial com arquivos placeholder
mkdir frontend
mkdir backend
New-Item frontend\.gitkeep -ItemType File   # PowerShell
New-Item backend\.gitkeep  -ItemType File   # PowerShell

# 4. Copie o README do template
# (copie o conteúdo de .github/templates/consolidated-repo-README.md para README.md)
Copy-Item ..\EstagioCheckFront\.github\templates\consolidated-repo-README.md README.md

# 5. Commit inicial
git add .
git commit -m "chore: inicializa estrutura do repositório consolidado"
git push origin main
```

Após o primeiro push nos repositórios fonte, as pastas serão preenchidas automaticamente.

---

## 9. Prevenção de loop infinito

**Problema:** Se o repo consolidado tivesse workflows, o commit feito pelo bot poderia
disparar novos workflows infinitamente.

**Solução adotada (dupla camada):**

1. **Tag `[sync-bot]` na mensagem de commit:**
   ```yaml
   if: "!contains(github.event.head_commit.message, '[sync-bot]')"
   ```
   O workflow verifica se a mensagem do commit que disparou o push contém `[sync-bot]`.
   Se sim, o job é ignorado. Todo commit feito pelo bot de sync inclui essa tag.

2. **Sem workflows no consolidado:**
   O repositório `EstagioCheck` não possui nenhum arquivo em `.github/workflows/`.
   Portanto, mesmo que o bot comite lá, nada é disparado.

**Por que as duas camadas?**  
A segunda é a principal proteção. A primeira é uma salvaguarda caso você decida
adicionar algum workflow ao consolidado no futuro.

---

## 10. Arquivos ignorados

Os seguintes padrões são excluídos pelo `rsync` e **nunca** chegam ao consolidado:

| Padrão | Motivo |
|---|---|
| `.git/` | Metadados do git — jamais deve ser copiado |
| `node_modules/` | Dependências — pesado e irrelevante |
| `dist/`, `build/`, `out/` | Artefatos de build |
| `.angular/` | Cache do Angular CLI |
| `bin/`, `obj/` | Saída de compilação .NET |
| `.cache/` | Cache genérico |
| `coverage/` | Relatórios de cobertura de testes |
| `.env`, `.env.*` | Segredos e variáveis de ambiente |
| `*.log` | Logs |
| `*.tmp`, `*.temp` | Arquivos temporários |
| `*.tsbuildinfo` | Cache de compilação TypeScript |
| `.sass-cache/` | Cache do compilador Sass |
| `.DS_Store`, `Thumbs.db` | Arquivos de sistema (macOS/Windows) |

> Para adicionar novos padrões, edite a seção `rsync` do workflow correspondente
> adicionando `--exclude='PADRÃO'`.

---

## 11. Deleção de arquivos

**Problema:** Se você deleta um arquivo no repositório fonte, ele precisa ser deletado
também no consolidado — caso contrário o consolidado ficará desatualizado com arquivos fantasmas.

**Solução:** A flag `--delete` do rsync resolve isso automaticamente.

```bash
rsync -av --delete source/ consolidated/frontend/
```

Como funciona:
- rsync compara o diretório fonte (`source/`) com o destino (`consolidated/frontend/`)
- Qualquer arquivo que existe no destino mas **não existe** na fonte é **removido**
- Isso espelha exatamente o estado atual do repositório fonte

**Segurança:** O `--delete` opera **apenas dentro** da pasta de destino (`/frontend` ou `/backend`).
Ele nunca apagará arquivos da outra pasta. Por exemplo, ao sincronizar o frontend,
o diretório `/backend` jamais é tocado.

---

## 12. Como testar

### Teste 1 — Execução manual do workflow

1. Acesse `github.com/RibeiroRenan04/EstagioCheckFront`
2. Clique em **Actions** → **Sync: Frontend → Consolidado**
3. Clique em **"Run workflow"** → **"Run workflow"**
4. Aguarde a execução (geralmente 30-60 segundos)
5. Verifique no repo consolidado se a pasta `/frontend` foi criada/atualizada

### Teste 2 — Push real

1. Faça qualquer alteração em qualquer arquivo do repositório frontend
2. Faça commit e push para a branch `main`
3. Acesse **Actions** e observe o workflow sendo disparado automaticamente
4. Verifique o consolidado

### Teste 3 — Deleção de arquivo

1. Delete um arquivo do repositório frontend
2. Faça commit e push para `main`
3. Verifique se o arquivo foi removido do consolidado

### Verificando logs

Em cada execução do workflow, os logs mostram:
- Quais arquivos foram copiados (`rsync` verbose)
- Se havia mudanças ou não
- O SHA do commit de origem
- Sucesso ou erro com código de saída

---

## 13. Erros comuns e soluções

### ❌ `Error: fatal: repository not found`
**Causa:** O nome do repositório consolidado está errado no workflow ou o PAT não tem acesso.  
**Solução:**
- Verifique o campo `repository:` no workflow: `RibeiroRenan04/EstagioCheck`
- Verifique se o PAT_TOKEN tem acesso de escrita ao repositório consolidado

### ❌ `Error: Input required and not supplied: token`
**Causa:** O secret `PAT_TOKEN` não existe no repositório fonte.  
**Solução:**
- Acesse `Settings → Secrets and variables → Actions` no repositório fonte
- Adicione o secret `PAT_TOKEN` com o valor do token gerado

### ❌ `Error: Authentication failed`
**Causa:** O PAT_TOKEN expirou ou foi revogado.  
**Solução:**
- Gere um novo token em `github.com/settings/tokens`
- Atualize o secret `PAT_TOKEN` nos repositórios fonte

### ❌ Workflow não dispara no push
**Causa 1:** O push foi feito em uma branch diferente de `main`.  
**Solução:** Altere a branch no workflow ou faça o push para `main`.

**Causa 2:** O commit foi feito pelo bot (contém `[sync-bot]`) — comportamento esperado.

### ❌ `rsync: command not found`
**Causa:** Raro em `ubuntu-latest`, mas pode ocorrer.  
**Solução:** Adicione um passo de instalação antes do rsync:
```yaml
- name: Instalar rsync
  run: sudo apt-get install -y rsync
```

### ❌ Arquivos do `/backend` desaparecendo ao sincronizar o frontend
**Causa:** O rsync está apontando para o diretório errado.  
**Solução:** Verifique se o destino é `consolidated/frontend/` e **não** `consolidated/`.

### ❌ `error: failed to push some refs` (conflict)
**Causa:** O frontend e o backend fizeram push simultâneo no consolidado.  
**Solução:** O campo `concurrency` no workflow garante que apenas uma sync roda por vez.
Se ainda ocorrer, adicione um `git pull --rebase` antes do push:
```yaml
- name: Pull antes do push
  working-directory: consolidated
  run: git pull --rebase origin main
```

---

## 14. Melhorias recomendadas

### Notificação por e-mail em falha
Adicione ao workflow para ser notificado quando a sync falhar:
```yaml
- name: Notificar falha
  if: failure()
  run: echo "::error::Falha na sincronização! Verifique os logs."
```

### Resumo na interface do GitHub Actions
```yaml
- name: Resumo da sincronização
  if: always()
  run: |
    echo "## Resultado da Sincronização 🔄" >> $GITHUB_STEP_SUMMARY
    echo "- **Origem:** \`${{ github.repository }}\`" >> $GITHUB_STEP_SUMMARY
    echo "- **Commit:** \`${{ github.sha }}\`" >> $GITHUB_STEP_SUMMARY
    echo "- **Branch:** \`${{ github.ref_name }}\`" >> $GITHUB_STEP_SUMMARY
```

### Renovação automática do PAT
Configure um lembrete no calendário para renovar o PAT antes do vencimento.
Para Fine-grained tokens, o GitHub envia e-mail automático 7 dias antes.

### Proteção do repositório consolidado
No repositório consolidado, em **Settings → Branches**:
- Adicione uma regra de proteção para `main`
- Marque **"Restrict who can push to matching branches"**
- Adicione apenas o usuário da conta (para garantir que somente o bot possa escrever)

---

## 15. Estrutura final esperada

```
EstagioCheckFront/                        (repositório frontend — ORIGEM)
├── .github/
│   ├── workflows/
│   │   └── sync-to-consolidated.yml     ✅ workflow de sync ativo
│   └── templates/
│       ├── backend-sync-workflow.yml    📋 template para o backend
│       └── consolidated-repo-README.md  📋 README para o consolidado
├── frontend/
│   └── (código Angular)
├── database/
│   └── (scripts SQL)
└── package.json

EstagioCheckBack/                         (repositório backend — ORIGEM)
├── .github/
│   └── workflows/
│       └── sync-to-consolidated.yml     ✅ workflow de sync ativo (copiar do template)
└── (código do backend)

EstagioCheck/                             (repositório CONSOLIDADO — DESTINO)
├── frontend/                            ← preenchido pelo workflow do EstagioCheckFront
│   ├── frontend/
│   ├── database/
│   └── package.json
├── backend/                             ← preenchido pelo workflow do EstagioCheckBack
│   └── (código do backend)
└── README.md
```

---

## Checklist de configuração

Use esta lista para garantir que tudo está configurado:

- [ ] Repositório consolidado `EstagioCheck` criado no GitHub
- [ ] Repositório consolidado inicializado com estrutura básica (ver seção 8)
- [ ] PAT_TOKEN gerado com escopo `repo` (ou fine-grained com `Contents: write`)
- [ ] Secret `PAT_TOKEN` adicionado em `EstagioCheckFront → Settings → Secrets`
- [ ] Secret `PAT_TOKEN` adicionado em `EstagioCheckBack → Settings → Secrets`
- [ ] Workflow do backend copiado de `.github/templates/backend-sync-workflow.yml`
      para `EstagioCheckBack/.github/workflows/sync-to-consolidated.yml`
- [ ] Nome do repositório consolidado corrigido nos dois workflows (`repository:` field)
- [ ] Permissões do Actions habilitadas em todos os repositórios (seção 5)
- [ ] Teste manual executado via `workflow_dispatch` e validado (seção 12)
- [ ] README do consolidado atualizado com informações reais

---

*Gerado em: 2026-05-19 | Sistema: GitHub Actions + rsync | Ambiente: Windows / GitHub*
