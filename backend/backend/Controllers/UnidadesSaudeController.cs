using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using EstagioCheck.API.Services.Geocoding;
using EstagioCheck.API.Services.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EstagioCheck.API.Controllers;

/// <summary>
/// Unidades de saúde onde o estágio acontece.
///
/// Opera sobre a mesma tabela usada pelo geofence do check-in ("Locais"): as
/// coordenadas cadastradas aqui são as que validam a presença do aluno.
///
/// Leitura liberada para todos os autenticados; escrita (cadastro, importação,
/// geocodificação, coordenadas) exclusiva do professor — a coordenadora consulta
/// e não altera.
/// </summary>
[ApiController]
[Route("api/unidades-saude")]
[Authorize]
public class UnidadesSaudeController(
    AppDbContext db,
    UnitGeocoder geocoder,
    UnidadeImportService importService,
    GeocodingQueue fila,
    ILogger<UnidadesSaudeController> logger) : ControllerBase
{
    // ── Listagem ──────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<List<UnidadeSaudeDto>>> GetAll(
        [FromQuery] string? nome,
        [FromQuery] string? tipo,
        [FromQuery] string? cidade,
        [FromQuery] bool? ativo,
        [FromQuery] string? statusGeocodificacao)
    {
        var query = db.Locations.AsQueryable();

        if (!string.IsNullOrWhiteSpace(nome))
            query = query.Where(l => EF.Functions.ILike(l.Name, $"%{nome.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(tipo))
            query = query.Where(l => l.Tipo == tipo);
        if (!string.IsNullOrWhiteSpace(cidade))
            query = query.Where(l => EF.Functions.ILike(l.Cidade!, $"%{cidade.Trim()}%"));
        if (ativo.HasValue)
            query = query.Where(l => l.Ativo == ativo.Value);
        if (!string.IsNullOrWhiteSpace(statusGeocodificacao))
            query = query.Where(l => l.StatusGeocodificacao == statusGeocodificacao);

        var unidades = await query.OrderBy(l => l.Name).ToListAsync();
        var contagem = await ContarEstagiariosAsync(unidades.Select(u => u.Id).ToList());

        return Ok(unidades.Select(u => Map(u, contagem.GetValueOrDefault(u.Id))));
    }

    /// <summary>
    /// Unidades cuja localização precisa de conferência — a tela de revisão manual.
    /// </summary>
    [HttpGet("pendentes-revisao")]
    public async Task<ActionResult<List<UnidadeSaudeDto>>> GetPendentesRevisao()
    {
        string[] statusPendentes =
        [
            StatusGeocodificacao.RevisaoManual,
            StatusGeocodificacao.NaoEncontrado,
            StatusGeocodificacao.Erro,
            StatusGeocodificacao.Pendente
        ];

        var unidades = await db.Locations
            .Where(l => l.StatusGeocodificacao != null && statusPendentes.Contains(l.StatusGeocodificacao))
            .OrderBy(l => l.Name)
            .ToListAsync();

        var contagem = await ContarEstagiariosAsync(unidades.Select(u => u.Id).ToList());
        return Ok(unidades.Select(u => Map(u, contagem.GetValueOrDefault(u.Id))));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UnidadeSaudeDto>> Get(Guid id)
    {
        var unidade = await db.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (unidade == null) return NotFound(new { message = "Unidade não encontrada." });

        var contagem = await ContarEstagiariosAsync([id]);
        return Ok(Map(unidade, contagem.GetValueOrDefault(id)));
    }

    // ── Cadastro manual ───────────────────────────────────────────────────────
    [HttpPost]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<UnidadeSaudeDto>> Create([FromBody] CriarUnidadeSaudeDto dto)
    {
        var unidade = new Location
        {
            Name = dto.Nome.Trim(),
            Tipo = dto.Tipo?.Trim(),
            Address = dto.Endereco?.Trim(),
            Numero = dto.Numero?.Trim(),
            Complemento = dto.Complemento?.Trim(),
            Bairro = dto.Bairro?.Trim(),
            Cidade = dto.Cidade?.Trim(),
            Uf = dto.Uf?.Trim().ToUpperInvariant(),
            Cep = dto.Cep?.Trim(),
            Telefone = dto.Telefone?.Trim(),
            RadiusMeters = dto.RaioMetros is > 0 ? dto.RaioMetros.Value : 100,
            IsInstitution = dto.EhInstituicao,
            ShiftStart = string.IsNullOrWhiteSpace(dto.InicioTurno) ? "07:00" : dto.InicioTurno,
            ShiftEnd = string.IsNullOrWhiteSpace(dto.FimTurno) ? "13:00" : dto.FimTurno,
            Ativo = true
        };

        // Coordenadas informadas na tela valem como definição manual e nunca são
        // sobrescritas depois por uma geocodificação automática.
        if (dto.Latitude.HasValue && dto.Longitude.HasValue)
        {
            unidade.Latitude = dto.Latitude.Value;
            unidade.Longitude = dto.Longitude.Value;
            unidade.OrigemCoordenadas = OrigemCoordenadas.Manual;
            unidade.StatusGeocodificacao = StatusGeocodificacao.Sucesso;
            unidade.GeocodificadoEm = BrasiliaTime.Agora;
        }
        else
        {
            unidade.StatusGeocodificacao = StatusGeocodificacao.Pendente;
        }

        db.Locations.Add(unidade);
        await db.SaveChangesAsync();

        if (unidade.StatusGeocodificacao == StatusGeocodificacao.Pendente && dto.GeocodificarAgora)
            fila.Enfileirar(unidade.Id);

        logger.LogInformation("Unidade de saúde {Unidade} cadastrada.", unidade.Name);
        return CreatedAtAction(nameof(Get), new { id = unidade.Id }, Map(unidade, 0));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<UnidadeSaudeDto>> Update(Guid id, [FromBody] AtualizarUnidadeSaudeDto dto)
    {
        var unidade = await db.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (unidade == null) return NotFound(new { message = "Unidade não encontrada." });

        var enderecoAntes = unidade.EnderecoCompleto;

        unidade.Name = dto.Nome.Trim();
        unidade.Tipo = dto.Tipo?.Trim();
        unidade.Address = dto.Endereco?.Trim();
        unidade.Numero = dto.Numero?.Trim();
        unidade.Complemento = dto.Complemento?.Trim();
        unidade.Bairro = dto.Bairro?.Trim();
        unidade.Cidade = dto.Cidade?.Trim();
        unidade.Uf = dto.Uf?.Trim().ToUpperInvariant();
        unidade.Cep = dto.Cep?.Trim();
        unidade.Telefone = dto.Telefone?.Trim();
        unidade.IsInstitution = dto.EhInstituicao;
        if (dto.RaioMetros is > 0) unidade.RadiusMeters = dto.RaioMetros.Value;
        if (!string.IsNullOrWhiteSpace(dto.InicioTurno)) unidade.ShiftStart = dto.InicioTurno;
        if (!string.IsNullOrWhiteSpace(dto.FimTurno)) unidade.ShiftEnd = dto.FimTurno;
        if (dto.Ativo.HasValue) unidade.Ativo = dto.Ativo.Value;
        unidade.UpdatedAt = BrasiliaTime.Agora;

        // Endereço mudou → a coordenada antiga deixa de valer, a menos que tenha
        // sido definida à mão: nesse caso quem decide é o administrador.
        if (unidade.EnderecoCompleto != enderecoAntes && !unidade.CoordenadaManual)
        {
            unidade.Latitude = 0;
            unidade.Longitude = 0;
            unidade.EnderecoGeocodificado = null;
            unidade.PrecisaoLocalizacao = null;
            unidade.GeocodificadoEm = null;
            unidade.StatusGeocodificacao = StatusGeocodificacao.Pendente;
            await db.SaveChangesAsync();
            fila.Enfileirar(unidade.Id);
        }
        else
        {
            await db.SaveChangesAsync();
        }

        var contagem = await ContarEstagiariosAsync([id]);
        return Ok(Map(unidade, contagem.GetValueOrDefault(id)));
    }

    /// <summary>
    /// Desativa a unidade. Não apagamos o registro: rodízios, presenças e alocações
    /// antigas apontam para ele e precisam continuar legíveis.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var unidade = await db.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (unidade == null) return NotFound(new { message = "Unidade não encontrada." });

        var alocacoesAtivas = await db.StudentAllocations.CountAsync(a => a.LocationId == id && a.Ativo);
        if (alocacoesAtivas > 0)
            return BadRequest(new
            {
                message = $"A unidade possui {alocacoesAtivas} estagiário(s) alocado(s). "
                        + "Encerre as alocações antes de desativá-la."
            });

        unidade.Ativo = false;
        unidade.UpdatedAt = BrasiliaTime.Agora;
        await db.SaveChangesAsync();

        logger.LogInformation("Unidade de saúde {Unidade} desativada.", unidade.Name);
        return NoContent();
    }

    // ── Geocodificação ────────────────────────────────────────────────────────
    [HttpPost("{id}/geocodificar")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<GeocodificacaoRespostaDto>> Geocodificar(
        Guid id, [FromQuery] bool sobrescreverManual = false, CancellationToken ct = default)
    {
        var unidade = await db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (unidade == null) return NotFound(new { message = "Unidade não encontrada." });

        // "Geocodificar novamente" é uma ação explícita: ignora cache e coordenada atual.
        var r = await geocoder.GeocodificarAsync(unidade, forcar: true,
            sobrescreverManual: sobrescreverManual, ct: ct);
        await db.SaveChangesAsync(ct);

        return Ok(new GeocodificacaoRespostaDto
        {
            Sucesso = r.Sucesso,
            Status = r.Status,
            Latitude = r.Latitude,
            Longitude = r.Longitude,
            EnderecoEncontrado = r.EnderecoEncontrado,
            Precisao = r.Precisao,
            Mensagem = r.Mensagem,
            VeioDoCache = r.VeioDoCache
        });
    }

    /// <summary>
    /// Prévia da localização de um endereço ainda não salvo — o botão "Buscar
    /// localização" do cadastro manual. Passa pelo backend como todo o resto: o
    /// frontend nunca fala com o Nominatim.
    /// </summary>
    [HttpPost("prever-endereco")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<GeocodificacaoRespostaDto>> PreverEndereco(
        [FromBody] PreverEnderecoDto dto, CancellationToken ct)
    {
        var provisoria = new Location
        {
            Name = dto.Nome?.Trim() ?? string.Empty,
            Address = dto.Endereco?.Trim(),
            Numero = dto.Numero?.Trim(),
            Bairro = dto.Bairro?.Trim(),
            Cidade = dto.Cidade?.Trim(),
            Uf = dto.Uf?.Trim(),
            Cep = dto.Cep?.Trim()
        };

        var r = await geocoder.GeocodificarAsync(provisoria, ct: ct);
        // Só o cache é persistido: a unidade ainda não existe.
        await db.SaveChangesAsync(ct);

        return Ok(new GeocodificacaoRespostaDto
        {
            Sucesso = r.Sucesso,
            Status = r.Status,
            Latitude = r.Latitude,
            Longitude = r.Longitude,
            EnderecoEncontrado = r.EnderecoEncontrado,
            Precisao = r.Precisao,
            Mensagem = r.Mensagem,
            VeioDoCache = r.VeioDoCache
        });
    }

    /// <summary>Aprova ou corrige as coordenadas à mão, na tela de revisão.</summary>
    [HttpPut("{id}/coordenadas")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<UnidadeSaudeDto>> DefinirCoordenadas(
        Guid id, [FromBody] DefinirCoordenadasDto dto)
    {
        var unidade = await db.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (unidade == null) return NotFound(new { message = "Unidade não encontrada." });

        unidade.Latitude = dto.Latitude;
        unidade.Longitude = dto.Longitude;
        unidade.OrigemCoordenadas = OrigemCoordenadas.Manual;
        unidade.StatusGeocodificacao = StatusGeocodificacao.Sucesso;
        unidade.GeocodificadoEm = BrasiliaTime.Agora;
        unidade.UpdatedAt = BrasiliaTime.Agora;
        if (!string.IsNullOrWhiteSpace(dto.Observacao))
            unidade.EnderecoGeocodificado = dto.Observacao.Trim();

        await db.SaveChangesAsync();

        logger.LogInformation("Coordenadas da unidade {Unidade} definidas manualmente.", unidade.Name);
        var contagem = await ContarEstagiariosAsync([id]);
        return Ok(Map(unidade, contagem.GetValueOrDefault(id)));
    }

    // ── Importação ────────────────────────────────────────────────────────────
    [HttpPost("importar/preview")]
    [Authorize(Roles = Roles.Supervisor)]
    [RequestSizeLimit(PlanilhaUnidadesReader.MaxBytes)]
    public async Task<ActionResult<ImportPreviewDto>> ImportarPreview(
        IFormFile? arquivo, CancellationToken ct)
    {
        if (arquivo == null || arquivo.Length == 0)
            return BadRequest(new { message = "Envie o arquivo da planilha no campo \"arquivo\"." });

        if (arquivo.Length > PlanilhaUnidadesReader.MaxBytes)
            return BadRequest(new
            {
                message = $"Arquivo maior que o limite de {PlanilhaUnidadesReader.MaxBytes / (1024 * 1024)} MB."
            });

        var extensao = Path.GetExtension(arquivo.FileName).ToLowerInvariant();
        if (!PlanilhaUnidadesReader.ExtensoesAceitas.Contains(extensao))
            return BadRequest(new { message = "Formato não aceito. Envie um arquivo .xlsx ou .csv." });

        await using var stream = arquivo.OpenReadStream();
        var (resultado, previewId) = await importService.GerarPreviaAsync(stream, arquivo.FileName, ct);

        if (resultado.Falhou)
            return BadRequest(new ImportPreviewDto { Erros = resultado.ErrosGerais, PodeConfirmar = false });

        var validas = resultado.Linhas.Count(l => l.Valida && !l.Duplicada);
        var duplicadas = resultado.Linhas.Count(l => l.Duplicada);

        return Ok(new ImportPreviewDto
        {
            PreviewId = previewId,
            TotalLinhas = resultado.Linhas.Count,
            Validas = validas,
            Invalidas = resultado.Linhas.Count(l => !l.Valida),
            Duplicadas = duplicadas,
            Erros = resultado.ErrosGerais,
            PodeConfirmar = validas > 0 || duplicadas > 0,
            Linhas = resultado.Linhas.Select(l => new ImportPreviewLinhaDto
            {
                Linha = l.Linha,
                Nome = l.Nome,
                Tipo = l.Tipo,
                EnderecoResumo = string.Join(", ", new[] { l.Endereco, l.Numero, l.Bairro, l.Cidade }
                    .Where(p => !string.IsNullOrWhiteSpace(p))),
                Cidade = l.Cidade,
                Cep = l.Cep,
                Status = l.Status,
                Erros = l.Erros,
                UnidadeExistenteId = l.UnidadeExistenteId
            }).ToList()
        });
    }

    [HttpPost("importar/confirmar")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<ImportacaoResultadoDto>> ImportarConfirmar(
        [FromBody] ConfirmarImportacaoDto dto, CancellationToken ct)
    {
        var previa = importService.RecuperarPrevia(dto.PreviewId);
        if (previa == null)
            return BadRequest(new { message = "Prévia expirada ou não encontrada. Envie a planilha novamente." });

        var atualizar = string.Equals(dto.AcaoDuplicadas, "atualizar", StringComparison.OrdinalIgnoreCase);
        var (loteId, criadas, atualizadas, ignoradas, enfileiradas) =
            await importService.ConfirmarAsync(previa, atualizar, ct);

        importService.DescartarPrevia(dto.PreviewId);

        return Ok(new ImportacaoResultadoDto
        {
            LoteId = loteId,
            Criadas = criadas,
            Atualizadas = atualizadas,
            Ignoradas = ignoradas,
            EnfileiradasParaGeocodificar = enfileiradas,
            Mensagem = enfileiradas > 0
                ? $"Importação iniciada. {enfileiradas} unidade(s) na fila de geocodificação — "
                  + "acompanhe o progresso nesta tela."
                : "Importação concluída. Nenhuma unidade precisou de geocodificação."
        });
    }

    /// <summary>Andamento da geocodificação de um lote, para a barra de progresso.</summary>
    [HttpGet("importar/{loteId}/progresso")]
    [Authorize(Roles = Roles.Gestao)]
    public async Task<ActionResult<ImportacaoProgressoDto>> ProgressoImportacao(Guid loteId)
    {
        var progresso = fila.ObterProgresso(loteId);
        if (progresso != null)
        {
            return Ok(new ImportacaoProgressoDto
            {
                LoteId = loteId,
                Total = progresso.Total,
                Processados = progresso.Processados,
                Pendentes = progresso.Pendentes,
                Sucesso = progresso.Sucesso,
                RevisaoManual = progresso.RevisaoManual,
                NaoEncontrado = progresso.NaoEncontrado,
                Erro = progresso.Erro,
                PercentualConcluido = progresso.PercentualConcluido,
                Concluido = progresso.Concluido
            });
        }

        // A fila é de memória: depois de um reinício, o andamento vem do banco.
        var unidades = await db.Locations
            .Where(l => l.LoteImportacao == loteId)
            .Select(l => l.StatusGeocodificacao)
            .ToListAsync();

        if (unidades.Count == 0) return NotFound(new { message = "Lote de importação não encontrado." });

        int Contar(string s) => unidades.Count(u => u == s);
        var pendentes = Contar(StatusGeocodificacao.Pendente) + Contar(StatusGeocodificacao.Processando);
        var processados = unidades.Count - pendentes;

        return Ok(new ImportacaoProgressoDto
        {
            LoteId = loteId,
            Total = unidades.Count,
            Processados = processados,
            Pendentes = pendentes,
            Sucesso = Contar(StatusGeocodificacao.Sucesso),
            RevisaoManual = Contar(StatusGeocodificacao.RevisaoManual),
            NaoEncontrado = Contar(StatusGeocodificacao.NaoEncontrado),
            Erro = Contar(StatusGeocodificacao.Erro),
            PercentualConcluido = unidades.Count == 0 ? 100 : 100 * processados / unidades.Count,
            Concluido = pendentes == 0
        });
    }

    /// <summary>Modelo oficial da planilha, em CSV.</summary>
    [HttpGet("importar/modelo")]
    [Authorize(Roles = Roles.Gestao)]
    public IActionResult BaixarModelo()
    {
        var linhas = new[]
        {
            string.Join(";", PlanilhaUnidadesReader.ColunasModelo),
            "UBS 1 Asa Norte;UBS;SGAN 906;S/N;;Asa Norte;Brasília;DF;70790-060;(61) 3550-0000",
            "UBS 2 Asa Sul;UBS;SGAS 612;S/N;;Asa Sul;Brasília;DF;70200-720;(61) 3550-0001"
        };
        var conteudo = "﻿" + string.Join("\r\n", linhas); // BOM: o Excel abre em UTF-8
        return File(System.Text.Encoding.UTF8.GetBytes(conteudo), "text/csv", "modelo-unidades-saude.csv");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private async Task<Dictionary<Guid, int>> ContarEstagiariosAsync(List<Guid> unidadeIds)
    {
        if (unidadeIds.Count == 0) return [];

        return await db.StudentAllocations
            .Where(a => a.Ativo && unidadeIds.Contains(a.LocationId))
            .GroupBy(a => a.LocationId)
            .Select(g => new { UnidadeId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.UnidadeId, x => x.Total);
    }

    internal static UnidadeSaudeDto Map(Location l, int estagiarios) => new()
    {
        Id = l.Id,
        Nome = l.Name,
        Tipo = l.Tipo,
        Endereco = l.Address,
        Numero = l.Numero,
        Complemento = l.Complemento,
        Bairro = l.Bairro,
        Cidade = l.Cidade,
        Uf = l.Uf,
        Cep = l.Cep,
        Telefone = l.Telefone,
        EnderecoCompleto = l.EnderecoCompleto,
        Latitude = l.Latitude,
        Longitude = l.Longitude,
        TemCoordenadas = l.TemCoordenadas,
        RaioMetros = l.RadiusMeters,
        OrigemCoordenadas = l.OrigemCoordenadas,
        StatusGeocodificacao = l.StatusGeocodificacao,
        EnderecoGeocodificado = l.EnderecoGeocodificado,
        PrecisaoLocalizacao = l.PrecisaoLocalizacao,
        GeocodificadoEm = l.GeocodificadoEm,
        EhInstituicao = l.IsInstitution,
        InicioTurno = l.ShiftStart,
        FimTurno = l.ShiftEnd,
        CodigoCnes = l.CodigoCnes,
        Ativo = l.Ativo,
        EstagiariosAtivos = estagiarios,
        CriadoEm = l.CreatedAt,
        AtualizadoEm = l.UpdatedAt
    };
}
