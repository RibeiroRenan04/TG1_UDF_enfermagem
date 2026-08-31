<div align="center">

# EstagioCheck

**Sistema de Controle de Estágio Curricular — UDF Enfermagem**

[![Backend](https://img.shields.io/badge/backend-.NET%208-512BD4?logo=dotnet)](https://estagiocheckapi-production.up.railway.app)
[![Frontend](https://img.shields.io/badge/frontend-Angular%2019-DD0031?logo=angular)](https://estagiocheck.vercel.app)
[![Database](https://img.shields.io/badge/database-PostgreSQL-336791?logo=postgresql)](https://www.postgresql.org)
[![Deploy API](https://img.shields.io/badge/deploy%20api-Railway-0B0D0E?logo=railway)](https://railway.app)
[![Deploy Web](https://img.shields.io/badge/deploy%20web-Vercel-000000?logo=vercel)](https://vercel.com)

</div>

---

## Sumário

- [Sobre o Sistema](#sobre-o-sistema)
- [Arquitetura](#arquitetura)
- [Stack Tecnológica](#stack-tecnológica)
- [Funcionalidades](#funcionalidades)
- [Perfis de Acesso](#perfis-de-acesso)
- [Estrutura de Repositórios](#estrutura-de-repositórios)
- [Estrutura do Projeto](#estrutura-do-projeto)
- [Endpoints da API](#endpoints-da-api)
- [Configuração Local — Backend](#configuração-local--backend)
- [Configuração Local — Frontend](#configuração-local--frontend)
- [Variáveis de Ambiente](#variáveis-de-ambiente)
- [Banco de Dados](#banco-de-dados)
- [Deploy em Produção](#deploy-em-produção)
- [Sincronização de Repositórios](#sincronização-de-repositórios)

---

## Sobre o Sistema

O **EstagioCheck** é uma plataforma web desenvolvida para gerenciar e controlar o estágio curricular dos estudantes de Enfermagem da UDF. O sistema substitui o controle manual em papel, oferecendo:

- Registro de presença com **validação por geolocalização**
- Acompanhamento formativo pelos preceptores
- Gestão de rodízios, grupos e locais de estágio
- Dashboard com métricas e histórico completo
- Relatórios exportáveis
- Notificações por e-mail

---

## Arquitetura

```
┌─────────────────────────────────────────────────────────────────────┐
│                          PRODUÇÃO                                    │
│                                                                      │
│   [Vercel]                          [Railway]                        │
│   Angular 19 SPA          ──────▶   .NET 8 REST API                │
│   estagiocheck.vercel.app   HTTPS   estagiocheckapi-production...   │
│                                          │                           │
│                                     [PostgreSQL]                     │
│                                     Railway Database                 │
└─────────────────────────────────────────────────────────────────────┘

Fluxo de autenticação:
  Login ──▶ JWT (8h) ──▶ Authorization: Bearer <token> em todas as chamadas
```

---

## Stack Tecnológica

### Backend

| Tecnologia | Versão | Uso |
|---|---|---|
| .NET / ASP.NET Core | 8.0 | Framework principal |
| Entity Framework Core | 8.0.11 | ORM |
| Npgsql | 8.0.11 | Driver PostgreSQL |
| JWT Bearer | 8.0.11 | Autenticação |
| BCrypt.Net-Next | 4.0.3 | Hash de senhas |
| MailKit | 4.16.0 | Envio de e-mails (SMTP) |
| Swashbuckle (Swagger) | 6.9.0 | Documentação da API |
| ClosedXML | 0.104.2 | Leitura das planilhas de unidades (.xlsx) |
| xUnit | 2.9.2 | Testes unitários (`backend.Tests`) |

### Frontend

| Tecnologia | Versão | Uso |
|---|---|---|
| Angular | 19.0 | Framework principal |
| Angular Material | 19.0 | Componentes de UI |
| Angular CDK | 19.0 | Primitivos de UI |
| RxJS | 7.8 | Programação reativa |
| XLSX | 0.18.5 | Exportação de planilhas |
| SCSS | — | Estilização |

---

## Funcionalidades

### Aluno
- **Check-in / Check-out** com validação de geolocalização (raio configurável por local)
- **Histórico de presenças** com status (aprovado / irregular / pendente)
- **Dashboard pessoal** com horas cumpridas, pendências e progresso
- Visualização de acompanhamentos formativos recebidos

### Preceptor
- **Painel de alunos** sob sua supervisão
- Registro e gestão de **acompanhamentos formativos**
- Validação e revisão de registros de presença
- Visualização de escalas e rodízios do grupo

### Supervisor (Coordenador)
- **Gestão completa de usuários** (cadastro individual e importação em lote via planilha)
- **Gestão de locais** de estágio com coordenadas GPS e raio de validação
- **Gestão de grupos** de alunos
- **Gestão de rodízios** (escalas de rotação por grupo/local/período)
- **Relatórios** exportáveis (presença, horas, irregularidades)
- Dashboard global com métricas consolidadas

### Sistema
- Autenticação JWT com expiração de 8 horas
- Fluxo de **primeiro acesso** com troca de senha obrigatória
- Recuperação de senha por **código via e-mail**
- CORS configurado para Vercel + localhost
- Integração com **BuscaSaúde** para pesquisa de localidades

---

## Perfis de Acesso

| Perfil | Identificador | Permissões |
|---|---|---|
| Aluno | `aluno` | Check-in/out, histórico próprio, dashboard pessoal, registro de irregularidades |
| Preceptor | `preceptor` | Acompanhamentos, visualização do grupo, ciência e observação de irregularidades |
| Professor (supervisor) | `supervisor` | Acesso total ao sistema; única decisão sobre presenças e irregularidades |
| Coordenadora | `coordenadora` | Mesma visão do professor, **somente leitura** (secretaria/estagiária) |

O controle de acesso é aplicado tanto no frontend (route guards) quanto no backend (claims JWT + `[Authorize(Roles)]`).

### Fluxo de irregularidades do ponto

O preceptor **não valida nem altera a situação** de uma irregularidade — ele registra
ciência e observação, e a ocorrência é encaminhada ao professor responsável:

```
1. Aluno registra a irregularidade  (ou o sistema gera a partir de um ponto fora das regras)
        ↓  status: aguardando_preceptor
2. Preceptor toma ciência
3. Preceptor insere justificativa/observação
4. Ocorrência encaminhada ao professor
        ↓  status: aguardando_professor
5. Professor analisa
6. Professor aprova, nega ou acrescenta parecer
        ↓  status: aprovada | negada
```

Aprovar uma ocorrência vinculada a um registro de ponto também regulariza esse registro.

### Horário e geolocalização do ponto

Os registros são gravados no **horário de Brasília (GMT-3)**, o fuso oficial do estágio —
não em UTC. Ver `backend/Services/BrasiliaTime.cs`; a migração `006_*.sql` converte os
registros antigos.

O ponto **só é aceito dentro do raio da unidade alocada**. Fora do raio a API recusa o
registro (`400`, `code: "fora_do_raio"`) e o app nem libera a câmera — quem tem um motivo
legítimo abre uma irregularidade para análise do professor.

### Permissão de atraso

Alunos previamente autorizados pelo professor (`PermissaoAtraso` em `Usuarios`) podem
chegar depois do horário previsto de início sem que o registro seja marcado como
irregularidade de horário. **A carga horária do dia continua sendo exigida** — a regra
vale apenas para o check-in; o check-out segue a janela normal do turno.

### Termo de responsabilidade

Preceptores, professores e coordenadoras precisam aceitar, no primeiro acesso, o termo
que registra que a senha é pessoal e intransferível e que respondem pelas ações feitas
com a própria conta (`GET /api/auth/terms`, `POST /api/auth/accept-terms`).

### Unidades de saúde e alocação de estagiários

Cadastro das unidades (manual ou por planilha `.xlsx`/`.csv`), geocodificação dos
endereços via **OpenStreetMap/Nominatim** e alocação de estagiários com histórico.

As unidades ficam na tabela **`Locais`** — a mesma que o check-in usa para o
geofence —, então a coordenada geocodificada já vale para validar a presença do
aluno. O Nominatim é consultado **apenas pelo backend**, com User-Agent
identificado, no máximo ~1 requisição por segundo e cache por endereço; a
geocodificação em massa roda em segundo plano e nunca entra no fluxo do check-in.

Documentação completa: [`docs/UNIDADES_SAUDE.md`](docs/UNIDADES_SAUDE.md) ·
planilha de exemplo: [`docs/exemplos/`](docs/exemplos/unidades-saude-exemplo.csv)

### Formato do RGM

O RGM não usa mais o prefixo **"14"**. A importação de planilhas aceita os dois formatos
e normaliza automaticamente; a migração `database/005_*.sql` limpa os registros antigos.

---

## Estrutura de Repositórios

O projeto é composto por três repositórios no GitHub:

```
RibeiroRenan04/EstagioCheckAPI        ← Backend (.NET) + Database (SQL)
RibeiroRenan04/<EstagioCheckFront>        ← Frontend (Angular)
RibeiroRenan04/TG1_UDF_enfermagem     ← Repositório Consolidado (espelho)
                ├── backend/
                └── frontend/
```

O repositório consolidado é atualizado automaticamente via **GitHub Actions** a cada push na branch `main` dos repositórios originais. Ele serve apenas como espelho unificado e não é utilizado para deploy.

---

## Estrutura do Projeto

### Backend (`/backend`)

```
backend/
├── Controllers/
│   ├── AuthController.cs         # Login, registro, recuperação de senha
│   ├── AttendanceController.cs   # Check-in/out e histórico de presenças
│   ├── DashboardController.cs    # Métricas e estatísticas
│   ├── EvaluationsController.cs  # Avaliações formativas
│   ├── FollowupsController.cs    # Acompanhamentos formativos
│   ├── GroupsController.cs       # Grupos de alunos
│   ├── LocationsController.cs    # Locais de estágio
│   ├── PreceptorController.cs    # Dados do painel do preceptor
│   ├── ReportsController.cs      # Relatórios exportáveis
│   └── UsersController.cs        # Gestão de usuários
├── Data/
│   └── AppDbContext.cs           # Contexto do EF Core
├── DTOs/                         # Data Transfer Objects
├── Migrations/                   # Migrações do EF Core
├── Models/                       # Entidades do domínio
│   ├── ApplicationUser.cs
│   ├── AttendanceRecord.cs
│   ├── Evaluation.cs
│   ├── FormativeFollowup.cs
│   ├── GroupMembership.cs
│   ├── Location.cs
│   ├── PasswordResetCode.cs
│   ├── RotationSchedule.cs
│   ├── StudentGroup.cs
│   └── StudentSemesterHistory.cs
├── Services/
│   ├── BuscaSaudeService.cs      # Integração BuscaSaúde
│   ├── EmailService.cs           # Envio de e-mails via SMTP
│   ├── GeoService.cs             # Validação de geolocalização
│   └── TokenService.cs           # Geração de JWT
├── Program.cs
└── EstagioCheck.API.csproj
```

### Frontend (`/frontend`)

```
frontend/src/app/
├── core/
│   ├── guards/
│   │   ├── auth.guard.ts         # Proteção de rotas autenticadas
│   │   └── role.guard.ts         # Controle de acesso por perfil
│   ├── interceptors/
│   │   └── auth.interceptor.ts   # Injeção automática do JWT
│   ├── models/
│   │   └── models.ts             # Interfaces TypeScript do domínio
│   └── services/                 # Serviços HTTP para cada módulo
├── features/
│   ├── auth/                     # Tela de login
│   ├── primeiro-acesso/          # Troca de senha obrigatória
│   ├── layout/                   # Shell da aplicação (sidebar, navbar)
│   ├── dashboard/                # Dashboard com métricas
│   ├── check-in/                 # Registro de presença com GPS
│   ├── historico/                # Histórico de presenças do aluno
│   ├── acompanhamentos/          # Acompanhamentos formativos
│   ├── preceptor/                # Painel do preceptor
│   ├── locais/                   # Gestão de locais (supervisor)
│   ├── rodizios/                 # Gestão de rodízios (supervisor)
│   ├── usuarios/                 # Gestão de usuários (supervisor)
│   └── relatorios/               # Relatórios exportáveis
└── app.routes.ts                 # Lazy loading por perfil
```

---

## Endpoints da API

Base URL (produção): `https://estagiocheckapi-production.up.railway.app/api`

| Método | Endpoint | Autenticação | Descrição |
|---|---|---|---|
| POST | `/auth/register` | Não | Cadastro de usuário |
| POST | `/auth/login` | Não | Login e geração de token |
| POST | `/auth/forgot-password` | Não | Solicitar código de recuperação |
| POST | `/auth/reset-password` | Não | Redefinir senha com código |
| POST | `/auth/change-password` | Sim | Trocar senha (primeiro acesso) |
| GET | `/attendance` | Sim | Listar registros de presença |
| POST | `/attendance/check-in` | Sim (aluno) | Registrar check-in |
| POST | `/attendance/check-out` | Sim (aluno) | Registrar check-out |
| GET | `/attendance/active-schedule` | Sim | Escala ativa no momento |
| GET | `/dashboard` | Sim | Estatísticas do dashboard |
| GET/POST | `/evaluations` | Sim | Avaliações formativas |
| GET/POST | `/followups` | Sim | Acompanhamentos formativos |
| GET/POST/PUT/DELETE | `/groups` | Sim | Grupos de alunos |
| GET/POST/PUT/DELETE | `/locations` | Sim | Locais de estágio |
| GET | `/preceptor` | Sim (preceptor) | Painel do preceptor |
| GET | `/reports` | Sim | Relatórios |
| GET/POST/PUT/DELETE | `/users` | Sim (professor) | Gestão de usuários |
| PATCH | `/users/{id}/late-permission` | Sim (professor) | Permissão de atraso do aluno |
| PATCH | `/users/{id}/shift` | Sim (professor) | Troca de turno do aluno |
| GET/POST | `/irregularities` | Sim | Irregularidades do ponto |
| PATCH | `/irregularities/{id}/preceptor-review` | Sim (preceptor) | Ciência + observação |
| PATCH | `/irregularities/{id}/professor-decision` | Sim (professor) | Aprovar ou negar |
| GET | `/followups/my-schedules` | Sim (preceptor) | Rodízios do preceptor e alunos alocados |
| GET/POST/PUT/DELETE | `/unidades-saude` | Sim | Unidades de saúde (escrita: professor) |
| POST | `/unidades-saude/importar/preview` | Sim (professor) | Prévia da planilha, sem gravar |
| POST | `/unidades-saude/importar/confirmar` | Sim (professor) | Confirma e enfileira geocodificação |
| POST | `/unidades-saude/{id}/geocodificar` | Sim (professor) | Geocodifica pelo Nominatim |
| GET/POST/DELETE | `/unidades-saude/{id}/estagiarios` | Sim | Alocação de estagiários |
| GET | `/alocacoes` | Sim (gestão) | Todas as alocações, com histórico |
| GET | `/estagiarios/{id}/unidade` | Sim | Unidade do estagiário (aluno: só a própria) |
| GET | `/auth/terms` | Não | Texto do termo de responsabilidade |
| POST | `/auth/accept-terms` | Sim | Registra o aceite do termo |

Documentação interativa disponível em: `/swagger` (somente em Development)

---

## Configuração Local — Backend

### Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [PostgreSQL 15+](https://www.postgresql.org/download/)

### Passos

```powershell
# 1. Clone o repositório
git clone https://github.com/RibeiroRenan04/EstagioCheckAPI.git
cd EstagioCheckAPI/backend

# 2. Crie o arquivo de configuração local
Copy-Item appsettings.Production.json.example appsettings.Development.json
# Edite appsettings.Development.json com suas credenciais (veja seção de variáveis)

# 3. Aplique as migrações
dotnet ef database update

# 4. Execute a aplicação
dotnet run
```

A API ficará disponível em `http://localhost:8080`.  
O Swagger estará em `http://localhost:8080/swagger`.

---

## Configuração Local — Frontend

### Pré-requisitos

- [Node.js 20+](https://nodejs.org)
- Angular CLI 19: `npm install -g @angular/cli`

### Passos

```powershell
# 1. Clone o repositório do frontend
git clone https://github.com/RibeiroRenan04/<FRONTEND_REPO>.git
cd <FRONTEND_REPO>/frontend

# 2. Instale as dependências
npm install

# 3. Execute em desenvolvimento
npm start
```

O frontend ficará disponível em `http://localhost:4200`.

> O arquivo `src/environments/environment.ts` já aponta para `http://localhost:8080/api`.

---

## Variáveis de Ambiente

### Backend — `appsettings.Development.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=estagio_check;Username=postgres;Password=SUA_SENHA"
  },
  "Jwt": {
    "Key": "sua-chave-secreta-com-minimo-32-caracteres",
    "Issuer": "EstagioCheck",
    "Audience": "EstagioCheckApp",
    "ExpiresInHours": 8
  },
  "Cors": {
    "Origins": [ "http://localhost:4200" ]
  },
  "Email": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": "587",
    "Username": "seu-email@gmail.com",
    "Password": "sua-senha-de-app",
    "FromName": "EstágioCheck UDF",
    "FromAddress": "seu-email@gmail.com"
  }
}
```

> Em produção (Railway), essas variáveis são configuradas como variáveis de ambiente no painel da plataforma.

---

## Banco de Dados

O banco de dados é **PostgreSQL** gerenciado pelo Entity Framework Core com migrações versionadas.

### Principais tabelas

| Tabela | Descrição |
|---|---|
| `Users` | Usuários do sistema (alunos, preceptores, supervisores) |
| `AttendanceRecords` | Registros de check-in/check-out |
| `Locations` | Locais de estágio com coordenadas GPS |
| `StudentGroups` | Grupos de alunos |
| `GroupMemberships` | Associação aluno ↔ grupo |
| `RotationSchedules` | Escalas de rodízio |
| `FormativeFollowups` | Acompanhamentos formativos |
| `Evaluations` | Avaliações |
| `PasswordResetCodes` | Códigos temporários de recuperação de senha |
| `StudentSemesterHistory` | Histórico semestral do aluno |
| `Irregularidades` | Ocorrências de ponto (fluxo aluno → preceptor → professor) |
| `AlocacoesEstagiarios` | Alocação de estagiários às unidades, com histórico |
| `GeocodificacaoCache` | Endereços já geocodificados, por endereço normalizado |

### Scripts SQL adicionais

```
database/
├── 002_udf_features.sql                  # Funções e features específicas da UDF
├── 003_rename_to_portuguese.sql          # Schema em português
├── 004_consolidar_rgm_remover_matricula.sql
├── 005_irregularidades_e_perfis.sql      # Irregularidades, permissão de atraso,
│                                         # perfil coordenadora, RGM sem o "14"
├── 006_fuso_brasilia.sql                 # Registros de ponto em GMT-3 (executar UMA vez)
└── 007_unidades_saude.sql                # Unidades de saúde, alocações e cache
                                          # de geocodificação
```

---

## Deploy em Produção

### Backend — Railway

| Item | Valor |
|---|---|
| Plataforma | [Railway](https://railway.app) |
| URL | `https://estagiocheckapi-production.up.railway.app` |
| Runtime | .NET 8 |
| Porta | Injetada automaticamente via variável `PORT` |

O `Program.cs` detecta a variável `PORT` do Railway automaticamente:
```csharp
var railwayPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(railwayPort))
    builder.WebHost.UseUrls($"http://0.0.0.0:{railwayPort}");
```

### Frontend — Vercel

| Item | Valor |
|---|---|
| Plataforma | [Vercel](https://vercel.com) |
| URL | `https://estagiocheck.vercel.app` |
| Framework | Angular (build: `ng build --configuration production`) |
| Output | `dist/estagio-check-web` |

---

## Sincronização de Repositórios

Os repositórios de backend e frontend são sincronizados automaticamente para o repositório consolidado `TG1_UDF_enfermagem` via GitHub Actions.

```
push (backend/main) ──▶ workflow ──▶ rsync ──▶ TG1_UDF_enfermagem/backend/
push (frontend/main) ──▶ workflow ──▶ rsync ──▶ TG1_UDF_enfermagem/frontend/
```

**Arquivos de workflow:**
- Backend: `.github/workflows/sync-to-consolidated.yml`
- Frontend: `.github/workflows/sync-to-consolidated.yml`

**Secret necessário** (em cada repositório de origem):  
`PAT_TOKEN` — Personal Access Token com escopo `repo`

Para o guia completo de configuração da sincronização, consulte [`docs/SYNC_SYSTEM_README.md`](docs/SYNC_SYSTEM_README.md).
