using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LocationsController(AppDbContext db, BuscaSaudeService buscaSaude) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<LocationDto>>> GetAll()
    {
        var locs = await db.Locations.OrderBy(l => l.Name).ToListAsync();
        return Ok(locs.Select(Map));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<LocationDto>> Get(Guid id)
    {
        var loc = await db.Locations.FindAsync(id);
        return loc == null ? NotFound() : Ok(Map(loc));
    }

    // Cadastro e edição de unidades ficam só em /api/unidades-saude (formulário,
    // importação e revisão de localização), onde as coordenadas são validadas e
    // ganham status de geocodificação. Os antigos POST/PUT/DELETE daqui gravavam
    // coordenadas sem conferência nenhuma e já não eram usados por tela alguma —
    // eram uma porta aberta para uma unidade com raio num lugar qualquer.

    // ── Busca Saúde DF (CNES / OpenDataSUS) ──────────────────────────────────
    /// <summary>Pesquisa unidades de saúde do DF via API pública do CNES (default: UBS).</summary>
    [HttpGet("busca-saude")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<List<BuscaSaudeEstabelecimentoDto>>> BuscarSaude(
        [FromQuery] string? q,
        [FromQuery] int tipo = BuscaSaudeService.TipoUbs,
        [FromQuery] int limit = 50)
    {
        var results = await buscaSaude.BuscarAsync(q, tipo, limit);
        return Ok(results);
    }

    /// <summary>Importa um estabelecimento do CNES como local de estágio.</summary>
    [HttpPost("import-from-busca-saude")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<LocationDto>> ImportFromBuscaSaude(
        [FromBody] ImportBuscaSaudeDto dto)
    {
        var cnes = Cnes.Normalizar(dto.CodigoCnes);
        if (cnes == null)
            return BadRequest(new { message = "Código CNES inválido." });

        // Procura nas duas grafias: cadastros antigos gravaram o código sem os zeros.
        var variantes = Cnes.Variantes(cnes);
        var alreadyExists = await db.Locations.AnyAsync(l => l.CodigoCnes != null && variantes.Contains(l.CodigoCnes));
        if (alreadyExists)
            return Conflict(new { message = "Estabelecimento já importado." });

        // O estabelecimento entra como unidade de saúde comum: "instituição" é a
        // instituição de ensino, marcada só nela — é o que a regra de sexta-feira
        // do ponto verifica. Marcar toda UBS como instituição anulava a regra.
        // Coordenada do CNES fora do intervalo (ou 0, 0) não conta: a unidade entra
        // pendente e vai para a revisão, em vez de nascer "confirmada" num lugar errado.
        var temCoordenadas = Coordenadas.Validar(dto.Latitude, dto.Longitude) == null;

        var loc = new Location
        {
            Name = dto.Nome.Trim(),
            Address = dto.Endereco?.Trim(),
            Latitude = temCoordenadas ? dto.Latitude : 0,
            Longitude = temCoordenadas ? dto.Longitude : 0,
            RadiusMeters = 150,
            IsInstitution = false,
            ShiftStart = "07:00",
            ShiftEnd = "19:00",
            CodigoCnes = cnes,
            Tipo = "UBS",
            Uf = "DF",
            Ativo = true,
            // As coordenadas vêm do CNES, não do Nominatim: registramos a origem
            // para a tela de unidades não pedir revisão do que já está conferido.
            OrigemCoordenadas = temCoordenadas ? Models.OrigemCoordenadas.Outro : null,
            StatusGeocodificacao = temCoordenadas
                ? Models.StatusGeocodificacao.Sucesso
                : Models.StatusGeocodificacao.Pendente,
            PrecisaoLocalizacao = temCoordenadas ? "CNES" : null,
            GeocodificadoEm = temCoordenadas ? BrasiliaTime.Agora : null
        };

        db.Locations.Add(loc);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = loc.Id }, Map(loc));
    }

    private static LocationDto Map(Location l) => new()
    {
        Id = l.Id, Name = l.Name, Address = l.Address,
        Latitude = l.Latitude, Longitude = l.Longitude,
        RadiusMeters = l.RadiusMeters, IsInstitution = l.IsInstitution,
        ShiftStart = l.ShiftStart, ShiftEnd = l.ShiftEnd,
        CodigoCnes = l.CodigoCnes
    };
}
