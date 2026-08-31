# Módulo de Unidades de Saúde

Cadastro das unidades onde o estágio acontece, geocodificação dos endereços via
OpenStreetMap/Nominatim e alocação de estagiários.

---

## Sumário

- [Onde as unidades ficam guardadas](#onde-as-unidades-ficam-guardadas)
- [Configuração do Nominatim](#configuração-do-nominatim)
- [Como executar a migration](#como-executar-a-migration)
- [Planilha de importação](#planilha-de-importação)
- [Fluxo da importação](#fluxo-da-importação)
- [Geocodificação](#geocodificação)
- [Alocação de estagiários](#alocação-de-estagiários)
- [Permissões](#permissões)
- [Endpoints](#endpoints)
- [Trocar o provedor de geocodificação](#trocar-o-provedor-de-geocodificação)
- [Testes](#testes)

---

## Onde as unidades ficam guardadas

O módulo usa a tabela **`Locais`**, que já existia — não uma tabela nova.

`Locais` sempre foi a unidade de saúde do sistema: é dela que o check-in lê
latitude, longitude e raio para validar a presença do aluno. Criar uma tabela
`UnidadesSaude` separada produziria duas fontes de coordenadas que divergiriam
com o tempo, e o check-in continuaria lendo a antiga. A migration `007` apenas
acrescenta a `Locais` o cadastro completo (tipo, bairro, cidade, UF, CEP,
telefone…) e os campos de geocodificação.

Consequências práticas:

- as unidades e rodízios já cadastrados continuam válidos, sem migração de dados;
- a coordenada geocodificada aqui **já vale** para o geofence do check-in;
- a API é exposta em `/api/unidades-saude`, com o vocabulário do módulo.

---

## Configuração do Nominatim

Seção `Geocoding` do `appsettings.json`:

```json
{
  "Geocoding": {
    "Provider": "Nominatim",
    "BaseUrl": "https://nominatim.openstreetmap.org",
    "UserAgent": "EstagioCheck-UDF/1.0 (contato@cs.udf.edu.br)",
    "RequestDelayMilliseconds": 1100,
    "TimeoutSeconds": 10,
    "MaxRetries": 2,
    "RetryAfterSecondsDefault": 60,
    "CountryCodes": "br",
    "AcceptLanguage": "pt-BR"
  }
}
```

### O User-Agent é obrigatório

A [política de uso do Nominatim](https://operations.osmfoundation.org/policies/nominatim/)
exige que cada aplicação se identifique **com um contato real**. Requisições sem
identificação são bloqueadas.

Formato: `NomeDoSistema/versão (e-mail-de-contato)`.

> **Antes de subir para produção, troque o e-mail** por um endereço realmente
> monitorado pela coordenação. Se o serviço detectar uso indevido, é para esse
> endereço que virá o aviso — e sem ele o bloqueio vem sem avisar.

### Em produção

Nunca deixe o contato apenas no `appsettings.json` versionado. Use
`appsettings.Production.json` ou variáveis de ambiente. No Railway, o separador
de seção é `__`:

```bash
Geocoding__UserAgent="EstagioCheck-UDF/1.0 (coordenacao.enfermagem@udf.edu.br)"
Geocoding__RequestDelayMilliseconds=1100
```

### Os demais parâmetros

| Chave | Para que serve |
|---|---|
| `RequestDelayMilliseconds` | Intervalo mínimo entre requisições. A política é de no máximo 1 req/s; 1100 ms dá folga. **Não reduza.** |
| `TimeoutSeconds` | Tempo máximo de espera por resposta. |
| `MaxRetries` | Tentativas extras após falha temporária. Deliberadamente baixo: insistir piora. |
| `RetryAfterSecondsDefault` | Espera após um HTTP 429. |
| `CountryCodes` | Restringe a busca ao país (`br`), reduzindo falsos positivos. |

---

## Como executar a migration

O projeto versiona o schema em **scripts SQL** (`database/NNN_*.sql`), não em
migrations do EF Core — o `Migrations/` do EF está desatualizado em relação ao
schema em português que roda em produção.

```bash
# Local
psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f database/007_unidades_saude.sql

# Supabase / Railway: cole o conteúdo no SQL Editor e execute
```

O script é **idempotente** — pode ser executado mais de uma vez. Ao final ele
imprime a conferência: unidades por status de geocodificação, quantas têm
coordenadas e quantas alocações estão ativas.

O que ele faz:

1. acrescenta as colunas de cadastro e geocodificação em `Locais`, com
   `CHECK` nos valores de `StatusGeocodificacao` e `OrigemCoordenadas`;
2. marca as unidades que **já tinham coordenadas** como `OrigemCoordenadas = MANUAL`
   — elas já validam check-in hoje e não podem ser sobrescritas por uma importação;
3. cria `AlocacoesEstagiarios`, com índice único garantindo **uma alocação ativa
   por estagiário** no banco, não só na API;
4. cria `GeocodificacaoCache`, com índice único por endereço normalizado.

---

## Planilha de importação

Formatos aceitos: **`.xlsx`** e **`.csv`** (separador `;` ou `,`).
Limites: **5 MB** e **2000 linhas**.

| Coluna | Obrigatória | Observação |
|---|---|---|
| `Nome` | **Sim** | Nome da unidade |
| `Tipo` | Não | UBS, Hospital, UPA… |
| `Endereco` | Ver nota | Logradouro |
| `Numero` | Não | |
| `Complemento` | Não | |
| `Bairro` | Não | |
| `Cidade` | Ver nota | |
| `UF` | Não | Duas letras |
| `CEP` | Não | Com ou sem hífen |
| `Telefone` | Não | |

> **Nota:** é obrigatório informar **`Endereco` ou `Cidade`**. Sem nenhum dos
> dois não há como localizar a unidade no mapa, e sem coordenada o check-in não
> pode ser validado ali.

O cabeçalho aceita variações de acento e caixa (`ENDEREÇO`, `endereco`,
`Município` para cidade). Baixe o modelo pronto em
`GET /api/unidades-saude/importar/modelo` ou no botão da tela de importação.

Exemplo: [`docs/exemplos/unidades-saude-exemplo.csv`](exemplos/unidades-saude-exemplo.csv)

---

## Fluxo da importação

```
Upload (.xlsx/.csv)
      ↓
Validação estrutural e por linha
      ↓
Prévia  ←── nada é gravado até aqui
      ↓
Confirmação do usuário
      ↓
Unidades criadas com status "pendente"
      ↓
Fila de geocodificação (BackgroundService)
      ↓
Nominatim, ~1 req/s
      ↓
sucesso | revisao_manual | nao_encontrado | erro
```

A resposta da confirmação é imediata: a geocodificação acontece em segundo plano
e o progresso é consultado em `GET /api/unidades-saude/importar/{loteId}/progresso`.
Uma planilha de 100 unidades leva cerca de dois minutos — isso é esperado e
deliberado.

### Duplicidades

A chave lógica é **nome + logradouro + número + cidade**, todos normalizados
(sem acento, caixa ou pontuação). Nome sozinho não basta: "UBS 1" existe em
várias regiões.

Na confirmação o usuário escolhe:

- **Ignorar** (padrão) — mantém o cadastro atual;
- **Atualizar** — regrava os dados com o que veio da planilha.

Em ambos os casos, **coordenadas de origem `MANUAL` nunca são substituídas**.
Se o endereço mudou e a coordenada era automática, ela é descartada e a unidade
volta para a fila.

---

## Geocodificação

### Quando acontece

Somente no **cadastro**, na **importação** ou quando o professor pede
explicitamente. **Nunca** durante o check-in do aluno e nunca ao abrir uma tela.

Se o Nominatim estiver fora do ar, o sistema continua funcionando normalmente
para todas as unidades que já têm coordenadas — a unidade afetada fica com
status `erro` e pode ser reprocessada depois. Nada é apagado.

### Cache

Antes de consultar o provedor, o sistema procura o **endereço normalizado** em
`GeocodificacaoCache`.

A chave é o **endereço**, não a consulta inteira: o nome da unidade entra na
consulta para ajudar o provedor a acertar o ponto, mas duas unidades no mesmo
endereço (anexos de um complexo, por exemplo) compartilham a mesma coordenada em
vez de gerar duas consultas. À precisão de um geofence de centenas de metros, o
mesmo endereço é o mesmo lugar.

Resultados "não encontrado" também vão para o cache: também não valem uma
segunda consulta automática.

> A ação **"Geocodificar novamente"** ignora o cache de propósito — é um pedido
> explícito do administrador para reconsultar o provedor.

### Escolha do resultado

O primeiro resultado do Nominatim **não** é adotado às cegas: uma busca por
"UBS 1" pode devolver a cidade inteira. O sistema ordena por especificidade
(`building`/`clinic` > `road` > `suburb` > `city`) e, quando o melhor resultado
ainda é genérico, marca a unidade como **`revisao_manual`** — a coordenada é
aproveitada, mas fica sinalizada para conferência.

### Status possíveis

| Status | Significado |
|---|---|
| `pendente` | Na fila, ainda não consultada |
| `processando` | Consulta em andamento |
| `sucesso` | Localização confiável |
| `revisao_manual` | Encontrou algo genérico demais; conferir |
| `nao_encontrado` | O provedor não achou o endereço |
| `erro` | Falha de comunicação; dá para tentar de novo |

### Origem das coordenadas

| Origem | Significado |
|---|---|
| `NOMINATIM` | Veio da geocodificação automática |
| `MANUAL` | Definida ou aprovada por uma pessoa — **nunca sobrescrita automaticamente** |
| `OUTRO` | Outra procedência (ex.: importação do CNES) |

---

## Alocação de estagiários

Convive com o rodízio da turma: o **rodízio** define a escala do grupo; a
**alocação** registra, aluno a aluno, em que unidade ele está e desde quando.

Regras validadas na API (não só na tela):

- só usuários com papel **`aluno`** podem ser alocados;
- cada estagiário tem no máximo **uma alocação ativa** — garantido também por
  índice único no banco;
- trocar de unidade **encerra** a alocação atual (grava `DataFim`) e cria outra:
  o histórico é preservado, nunca sobrescrito;
- transferir alguém que já tem unidade exige confirmação explícita
  (`encerrarAlocacaoAtual`), senão a API responde `409`.

---

## Permissões

| Ação | Professor (`supervisor`) | Coordenadora | Preceptor | Aluno |
|---|:---:|:---:|:---:|:---:|
| Ver unidades | ✓ | ✓ | ✓ | ✓ |
| Cadastrar / editar / desativar | ✓ | — | — | — |
| Importar planilha | ✓ | — | — | — |
| Geocodificar / ajustar coordenadas | ✓ | — | — | — |
| Ver alocações (todas) | ✓ | ✓ | — | — |
| Alocar / encerrar alocação | ✓ | — | — | — |
| Ver a própria unidade | — | — | — | ✓ |

A coordenadora enxerga tudo que o professor enxerga e **não altera nada** — a
regra vale na API, não apenas no frontend.

---

## Endpoints

| Método | Rota | Perfil |
|---|---|---|
| GET | `/api/unidades-saude` | autenticado |
| GET | `/api/unidades-saude/{id}` | autenticado |
| GET | `/api/unidades-saude/pendentes-revisao` | autenticado |
| POST | `/api/unidades-saude` | professor |
| PUT | `/api/unidades-saude/{id}` | professor |
| DELETE | `/api/unidades-saude/{id}` | professor |
| POST | `/api/unidades-saude/importar/preview` | professor |
| POST | `/api/unidades-saude/importar/confirmar` | professor |
| GET | `/api/unidades-saude/importar/{loteId}/progresso` | gestão |
| GET | `/api/unidades-saude/importar/modelo` | gestão |
| POST | `/api/unidades-saude/{id}/geocodificar` | professor |
| POST | `/api/unidades-saude/prever-endereco` | professor |
| PUT | `/api/unidades-saude/{id}/coordenadas` | professor |
| GET | `/api/unidades-saude/{id}/estagiarios` | autenticado |
| GET | `/api/unidades-saude/{id}/estagiarios-disponiveis` | gestão |
| POST | `/api/unidades-saude/{id}/estagiarios` | professor |
| DELETE | `/api/unidades-saude/{id}/estagiarios/{idEstagiario}` | professor |
| GET | `/api/alocacoes` | gestão |
| GET | `/api/estagiarios/{id}/unidade` | autenticado (aluno: só a própria) |
| GET | `/api/estagiarios/{id}/alocacoes` | autenticado (aluno: só as próprias) |

> O frontend **nunca** chama `nominatim.openstreetmap.org` diretamente. Toda
> geocodificação passa pela API, que concentra o User-Agent, o limite de uso, o
> cache e o tratamento de erros.

---

## Trocar o provedor de geocodificação

A aplicação depende apenas da interface `IGeocodingService`:

```csharp
public interface IGeocodingService
{
    string Provedor { get; }
    Task<GeocodingResult?> GeocodeAsync(string address, CancellationToken cancellationToken);
}
```

Para migrar para Google, Azure Maps ou HERE:

1. crie `GoogleGeocodingService : IGeocodingService` em `Services/Geocoding/`;
2. troque **uma linha** no `Program.cs`:
   ```csharp
   builder.Services.AddScoped<IGeocodingService, GoogleGeocodingService>();
   ```
3. ajuste a seção `Geocoding` do `appsettings.json`.

Nenhum controller, tela ou regra de negócio muda. O cache guarda o provedor em
cada entrada, então dá para invalidar as antigas se preciso.

---

## Testes

```bash
dotnet test backend.Tests/EstagioCheck.API.Tests.csproj
```

Cobrem geocodificação (encontrado, não encontrado, timeout, 429, JSON inválido,
cache presente/ausente, coordenada manual preservada), importação (planilha
válida, extensão e conteúdo inválidos, coluna ausente, linha inválida,
duplicidade, planilha vazia, injeção de fórmula) e alocação (aluno válido,
perfil não-aluno, unidade inexistente, já alocado, encerramento, troca de
unidade com histórico).
