using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using EstagioCheck.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EstagioCheck.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AttendanceController(
    AppDbContext db, GeoService geo, ProgramacaoService programacao, EscopoPreceptorService escopoPreceptor)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AttendanceRecordDto>>> GetAll([FromQuery] Guid? studentId, [FromQuery] int limit = 200)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "aluno";

        var query = db.AttendanceRecords
            .Include(r => r.Student)
            .Include(r => r.Location)
            .Include(r => r.ValidatedBy)
            .AsQueryable();

        if (role == "aluno")
            query = query.Where(r => r.StudentId == userId);
        else
        {
            if (role == Roles.Preceptor)
                query = EscopoPreceptorService.FiltrarPontos(query, await escopoPreceptor.CarregarAsync(userId));
            if (studentId.HasValue)
                query = query.Where(r => r.StudentId == studentId.Value);
        }

        var recs = await query
            .Include(r => r.Schedule)
            .Include(r => r.RemoteActivity)
            .OrderByDescending(r => r.RecordedAt)
            .Take(limit)
            .ToListAsync();

        var irregularidades = await IrregularidadesPorPontoAsync(recs.Select(r => r.Id).ToList());

        return Ok(recs.Select(r => Map(r, irregularidades.GetValueOrDefault(r.Id))));
    }

    private async Task<Dictionary<Guid, PointIrregularity>> IrregularidadesPorPontoAsync(List<Guid> recordIds)
    {
        if (recordIds.Count == 0) return new Dictionary<Guid, PointIrregularity>();

        var ocorrencias = await db.PointIrregularities
            .Where(i => i.AttendanceRecordId != null && recordIds.Contains(i.AttendanceRecordId.Value))
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return ocorrencias
            .GroupBy(i => i.AttendanceRecordId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Escala ativa já resolvida pela programação do dia (regra semanal + exceções do calendário).</summary>
    [HttpGet("active-schedule")]
    public async Task<ActionResult<ActiveScheduleDto?>> GetActiveSchedule()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);

        var dia = await programacao.ObterAsync(userId, BrasiliaTime.Hoje);
        if (dia.ScheduleId == null) return Ok(null);

        var horasExigidas = await db.RotationSchedules
            .Where(s => s.Id == dia.ScheduleId)
            .Select(s => s.RequiredHours)
            .FirstOrDefaultAsync();

        return Ok(new ActiveScheduleDto
        {
            ScheduleId = dia.ScheduleId.Value,
            Shift = dia.Turno,
            PeriodLabel = dia.PeriodLabel ?? string.Empty,
            ActivityType = dia.ActivityType ?? string.Empty,
            RequiredHours = horasExigidas,
            Mode = dia.Modo,
            ModeLabel = ModoAtividade.Rotulo(dia.Modo),
            Validation = dia.Validacao,
            Reason = dia.Motivo,
            Location = dia.Local == null ? null : new LocationDto
            {
                Id = dia.Local.Id,
                Name = dia.Local.Name,
                Address = dia.Local.Address,
                Latitude = dia.Local.Latitude,
                Longitude = dia.Local.Longitude,
                RadiusMeters = dia.Local.RadiusMeters,
                IsInstitution = dia.Local.IsInstitution,
                ShiftStart = dia.Local.ShiftStart,
                ShiftEnd = dia.Local.ShiftEnd,
                CodigoCnes = dia.Local.CodigoCnes
            }
        });
    }

    [HttpGet("open-check-in")]
    public async Task<ActionResult> GetOpenCheckIn()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);

        var status = await MontarStatusDoTurnoAsync(userId);

        if (status.CheckInId.HasValue && !status.CheckOutId.HasValue)
            return Ok(new { id = status.CheckInId.Value, recorded_at = status.CheckInAt });

        return Ok(null);
    }

    // Mesma trava do POST: no máximo 1 check-in e 1 check-out por turno.
    [HttpGet("shift-status")]
    public async Task<ActionResult<ShiftPointStatusDto>> GetShiftStatus()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);

        return Ok(await MontarStatusDoTurnoAsync(userId));
    }

    [HttpPost]
    public async Task<ActionResult<AttendanceRecordDto>> Create([FromBody] CreateAttendanceDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);

        if (dto.Type != "check_in" && dto.Type != "check_out")
            return BadRequest(new { message = "Tipo inválido." });

        var agora = BrasiliaTime.Agora;

        // Ponto por localização só em dia presencial; dia remoto se comprova pelo código da atividade.
        var dia = await programacao.ObterAsync(userId, DateOnly.FromDateTime(agora));

        // Sem programação, o aluno escolhe a unidade na tela (ainda sujeito ao raio).
        var temProgramacao = dia.ScheduleId.HasValue || dia.ExcecaoId.HasValue;

        if (temProgramacao && dia.Modo == ModoAtividade.SemAtividade)
            return BadRequest(new
            {
                message = dia.Motivo is null
                    ? "Não há atividade programada para hoje: nenhum ponto é exigido."
                    : $"{dia.Motivo} Não há ponto a registrar hoje.",
                code = "sem_atividade_programada",
                mode = dia.Modo
            });

        if (dia.Modo == ModoAtividade.Remoto)
            return BadRequest(new
            {
                message = "Hoje é dia de atividade remota: a presença é registrada com o código "
                        + "informado pelo professor, não pela localização.",
                code = "dia_remoto",
                mode = dia.Modo
            });

        var descricao = dto.ActivitiesDescription?.Trim();
        if (dto.Type == "check_out" && string.IsNullOrWhiteSpace(descricao))
            return BadRequest(new
            {
                message = "Descreva as atividades realizadas no turno para finalizar o check-out.",
                code = "descricao_obrigatoria"
            });

        // Trava de registro: no máximo 1 check-in e 1 check-out por turno.
        var turno = await TurnoDoRegistroAsync(dto.ScheduleId, agora);
        var registrosDoTurno = await RegistrosDoTurnoAsync(userId, DateOnly.FromDateTime(agora), turno);

        var jaRegistrado = registrosDoTurno.FirstOrDefault(r => r.Tipo == dto.Type);
        if (jaRegistrado != null)
            return Conflict(new
            {
                message = dto.Type == "check_in"
                    ? $"Você já registrou o check-in do turno da {Turnos.Rotulo(turno)} às "
                      + $"{jaRegistrado.RegistradoEm:HH\\:mm}. É permitido apenas um por turno."
                    : $"Você já registrou o check-out do turno da {Turnos.Rotulo(turno)} às "
                      + $"{jaRegistrado.RegistradoEm:HH\\:mm}. É permitido apenas um por turno.",
                code = "registro_duplicado_no_turno",
                shift = turno
            });

        if (dto.Type == "check_out" && !registrosDoTurno.Any(r => r.Tipo == "check_in"))
            return BadRequest(new
            {
                message = $"Não há check-in registrado no turno da {Turnos.Rotulo(turno)}. "
                        + "Registre a entrada antes do check-out ou abra uma irregularidade.",
                code = "sem_check_in_no_turno",
                shift = turno
            });

        var aluno = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);

        // A unidade que vale é a da programação do dia, não o local fixo do rodízio.
        var location = dia.Local
            ?? (dto.LocationId.HasValue ? await db.Locations.FindAsync(dto.LocationId.Value) : null);

        if (dia.Local != null && dto.LocationId.HasValue && dto.LocationId.Value != dia.Local.Id)
            return BadRequest(new
            {
                message = $"Hoje o registro é em {dia.Local.Name}"
                        + (dia.Motivo is null ? "." : $" ({dia.Motivo})."),
                code = "local_divergente",
                locationId = dia.Local.Id,
                locationName = dia.Local.Name
            });
        if (location == null)
            return BadRequest(new
            {
                message = "Não há unidade definida para o seu ponto hoje, então a localização não pode ser "
                        + "validada. Procure a coordenação para conferir o seu rodízio; se esteve em "
                        + "atividade, registre uma irregularidade.",
                code = "sem_unidade"
            });

        // Sem localização confirmada as coordenadas são (0, 0) ou duvidosas: medir o raio
        // culparia o aluno por um erro de cadastro.
        if (!location.LocalizacaoConfirmada)
            return BadRequest(new
            {
                message = $"A unidade {location.Name} ainda não tem a localização confirmada no sistema, "
                        + "então o ponto não pode ser validado pelo raio. Avise a coordenação; se esteve "
                        + "em atividade, registre uma irregularidade.",
                code = "unidade_sem_localizacao",
                locationName = location.Name
            });

        // Fora do raio não há registro; motivo legítimo vira irregularidade para análise.
        {
            var distancia = geo.HaversineMeters(dto.Latitude, dto.Longitude, location.Latitude, location.Longitude);
            var precisaoGps = ToleranciaGps(dto.AccuracyMeters);
            if (Math.Max(0, distancia - precisaoGps) > location.RadiusMeters)
            {
                return BadRequest(new
                {
                    message = $"Você está a {distancia:0} m de {location.Name} (limite de {location.RadiusMeters} m). "
                            + "Aproxime-se da unidade para registrar o ponto. Se houver um motivo, registre uma irregularidade.",
                    code = "fora_do_raio",
                    distanceMeters = Math.Round(distancia),
                    radiusMeters = location.RadiusMeters,
                    locationName = location.Name
                });
            }
        }

        // A regra fixa de sexta-feira só continua valendo em rodízios sem programação
        // semanal: com ela cadastrada, é a programação que diz onde é o dia.
        var temProgramacaoSemanal = dia.ScheduleId.HasValue
            && await db.RotationDaySchedules.AnyAsync(d => d.ScheduleId == dia.ScheduleId.Value);

        var (status, irregularityReason, distanceMeters) = AvaliarRegistro(
            location, dto.Latitude, dto.Longitude, dto.AccuracyMeters, agora,
            dto.Type, turno, aluno?.AllowLateArrival == true, temProgramacaoSemanal);

        // Limite de ~5 MB; o frontend já comprime a imagem.
        const int MaxFotoChars = 7_000_000;
        string? photoUrl = null;
        if (!string.IsNullOrEmpty(dto.PhotoBase64) && dto.PhotoBase64.Length <= MaxFotoChars)
            photoUrl = dto.PhotoBase64;

        var record = new AttendanceRecord
        {
            StudentId = userId,
            ScheduleId = dto.ScheduleId ?? dia.ScheduleId,
            LocationId = location?.Id ?? dto.LocationId,
            Type = dto.Type,
            RecordedAt = agora,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            DistanceMeters = distanceMeters,
            PhotoUrl = photoUrl,
            ActivitiesDescription = string.IsNullOrWhiteSpace(descricao) ? null : descricao,
            Status = status,
            IrregularityReason = irregularityReason
        };

        db.AttendanceRecords.Add(record);

        if (status == "irregular")
        {
            db.PointIrregularities.Add(new PointIrregularity
            {
                StudentId = userId,
                AttendanceRecordId = record.Id,
                ScheduleId = dto.ScheduleId,
                Type = "fora_do_local",
                OccurredOn = DateOnly.FromDateTime(agora),
                Description = irregularityReason ?? "Registro de ponto fora das regras.",
                Status = PointIrregularity.StatusAguardandoPreceptor
            });
        }

        await db.SaveChangesAsync();

        await db.Entry(record).Reference(r => r.Student).LoadAsync();
        if (record.LocationId.HasValue)
            await db.Entry(record).Reference(r => r.Location).LoadAsync();
        if (record.ScheduleId.HasValue)
            await db.Entry(record).Reference(r => r.Schedule).LoadAsync();

        return Ok(Map(record));
    }

    /// <summary>Exclusiva do professor: o preceptor só observa a ocorrência.</summary>
    [HttpPatch("{id}/validate")]
    [Authorize(Roles = Roles.Supervisor)]
    public async Task<ActionResult<AttendanceRecordDto>> Validate(Guid id, [FromBody] ValidateAttendanceDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);

        var record = await db.AttendanceRecords
            .Include(r => r.Student)
            .Include(r => r.Location)
            .Include(r => r.Schedule)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (record == null) return NotFound();

        record.Status = dto.Approve ? "aprovado" : "irregular";
        if (!dto.Approve && !string.IsNullOrEmpty(dto.Reason))
            record.IrregularityReason = dto.Reason;
        record.ValidatedById = userId;
        record.ValidatedAt = BrasiliaTime.Agora;

        await db.SaveChangesAsync();
        return Ok(Map(record));
    }

    private const int ToleranciaTurnoMin = 30;

    public const double ToleranciaGpsMaximaMetros = 50;

    /// <summary>Teto para o desconto da precisão do aparelho: sem ele, "precisão de 10 km" passava em qualquer raio.</summary>
    public static double ToleranciaGps(double? precisaoInformada) =>
        Math.Clamp(precisaoInformada.GetValueOrDefault(0), 0, ToleranciaGpsMaximaMetros);

    /// <summary>
    /// Fora do raio, ou sexta fora da instituição (só sem programação semanal) → "irregular";
    /// dentro do raio mas fora da janela do turno → "pendente"; senão → "aprovado".
    /// A permissão de atraso só dispensa a chegada tardia.
    /// </summary>
    private (string status, string? reason, double? distance) AvaliarRegistro(
        Location? location, double lat, double lon, double? accuracyMeters, DateTime recordedAt,
        string tipo, string turno, bool permiteAtraso = false, bool temProgramacaoSemanal = false)
    {
        if (location == null)
            return ("pendente", "Sem local vinculado. Aguardando validação manual.", null);

        var distance = geo.HaversineMeters(lat, lon, location.Latitude, location.Longitude);
        var motivos = new List<string>();

        var precisao = ToleranciaGps(accuracyMeters);
        var distanciaEfetiva = Math.Max(0, distance - precisao);
        var foraDoRaio = distanciaEfetiva > location.RadiusMeters;
        if (foraDoRaio)
            motivos.Add($"Fora do raio ({distance:0}m, precisão GPS ±{precisao:0}m; limite {location.RadiusMeters}m)");

        var horaLocal = recordedAt.TimeOfDay;
        var foraDoTurno = false;
        // A janela é a do turno do registro: o horário da unidade vale para o turno
        // em que ele começa; os outros turnos usam a janela padrão (ver Turnos).
        if (Turnos.JanelaNaUnidade(location.ShiftStart, location.ShiftEnd, turno) is var (inicio, fim))
        {
            var tol = TimeSpan.FromMinutes(ToleranciaTurnoMin);
            var antesDoInicio = horaLocal < inicio - tol;
            // A permissão de atraso libera apenas a chegada tardia (o limite inferior
            // do turno); chegar antes ou sair depois continua fora da janela.
            var depoisDoFim = horaLocal > fim + tol;
            var atrasado = tipo == "check_in" && !antesDoInicio && !depoisDoFim
                        && horaLocal > inicio + tol;

            if (antesDoInicio || depoisDoFim)
            {
                foraDoTurno = true;
                motivos.Add($"Registro às {horaLocal:hh\\:mm} fora do turno ({inicio:hh\\:mm}–{fim:hh\\:mm})");
            }
            else if (atrasado && !permiteAtraso)
            {
                foraDoTurno = true;
                motivos.Add($"Chegada às {horaLocal:hh\\:mm}, após o início do turno ({inicio:hh\\:mm})");
            }
        }

        var sextaForaInstituicao = !temProgramacaoSemanal
            && recordedAt.DayOfWeek == DayOfWeek.Friday && !location.IsInstitution;
        if (sextaForaInstituicao)
            motivos.Add("Sexta-feira: o registro deve ser feito na instituição de ensino");

        if (foraDoRaio || sextaForaInstituicao)
            return ("irregular", string.Join("; ", motivos), distance);
        if (foraDoTurno)
            return ("pendente", string.Join("; ", motivos), distance);
        return ("aprovado", null, distance);
    }

    private static string ShiftFromHour(int h) => Turnos.DaHora(h);

    /// <summary>Turno da escala vinculada; sem escala, o turno do horário do registro.</summary>
    private async Task<string> TurnoDoRegistroAsync(Guid? scheduleId, DateTime momento)
    {
        if (scheduleId.HasValue)
        {
            var turnoEscala = await db.RotationSchedules
                .Where(sc => sc.Id == scheduleId.Value)
                .Select(sc => sc.Shift)
                .FirstOrDefaultAsync();

            var normalizado = Turnos.Normalizar(turnoEscala);
            if (normalizado != null) return normalizado;
        }

        return Turnos.DaHora(momento);
    }

    private sealed record RegistroDoTurno(Guid Id, string Tipo, DateTime RegistradoEm);

    private async Task<List<RegistroDoTurno>> RegistrosDoTurnoAsync(Guid studentId, DateOnly dia, string turno)
    {
        var inicio = dia.ToDateTime(TimeOnly.MinValue);
        var fim = inicio.AddDays(1);

        var doDia = await db.AttendanceRecords
            .Where(r => r.StudentId == studentId && r.RecordedAt >= inicio && r.RecordedAt < fim)
            .Select(r => new
            {
                r.Id,
                r.Type,
                r.RecordedAt,
                TurnoEscala = r.Schedule != null ? r.Schedule.Shift : null
            })
            .ToListAsync();

        return [.. doDia
            .Where(r => (Turnos.Normalizar(r.TurnoEscala) ?? Turnos.DaHora(r.RecordedAt)) == turno)
            .OrderBy(r => r.RecordedAt)
            .Select(r => new RegistroDoTurno(r.Id, r.Type, r.RecordedAt))];
    }

    private async Task<ShiftPointStatusDto> MontarStatusDoTurnoAsync(Guid studentId)
    {
        var agora = BrasiliaTime.Agora;
        var hoje = DateOnly.FromDateTime(agora);

        // A escala ativa manda no turno; sem escala, vale o horário do relógio.
        var turmas = await db.GroupMemberships
            .Where(m => m.StudentId == studentId)
            .Select(m => m.GroupId)
            .Distinct()
            .ToListAsync();
        var turnoDoRelogio = Turnos.DaHora(agora);
        var turno = turnoDoRelogio;

        if (turmas.Count > 0)
        {
            // O aluno pode estar em duas turmas: consideramos as escalas de todas,
            // que é o que separa o rodízio da manhã do da tarde.
            var turnosHoje = await db.RotationSchedules
                .Where(sc => turmas.Contains(sc.GroupId)
                          && sc.StartDate <= hoje && sc.EndDate >= hoje)
                .Select(sc => sc.Shift)
                .ToListAsync();

            var normalizados = turnosHoje.Select(Turnos.Normalizar).Where(t => t != null).ToList();
            // Se houver escala no turno do relógio, é ela; senão, a única escala do dia.
            if (!normalizados.Contains(turnoDoRelogio) && normalizados.Count == 1)
                turno = normalizados[0]!;
        }

        var registros = await RegistrosDoTurnoAsync(studentId, hoje, turno);
        var checkIn = registros.FirstOrDefault(r => r.Tipo == "check_in");
        var checkOut = registros.FirstOrDefault(r => r.Tipo == "check_out");

        var podeCheckIn = checkIn == null;
        var podeCheckOut = checkIn != null && checkOut == null;
        var fechado = checkIn != null && checkOut != null;

        return new ShiftPointStatusDto
        {
            Shift = turno,
            ShiftLabel = Turnos.Rotulo(turno),
            Date = hoje,
            CheckInId = checkIn?.Id,
            CheckInAt = checkIn?.RegistradoEm,
            CheckOutId = checkOut?.Id,
            CheckOutAt = checkOut?.RegistradoEm,
            CanCheckIn = podeCheckIn,
            CanCheckOut = podeCheckOut,
            ShiftClosed = fechado,
            BlockedReason = fechado
                ? $"O turno da {Turnos.Rotulo(turno)} já tem check-in e check-out registrados. "
                  + "Se algo estiver errado, abra uma irregularidade."
                : null
        };
    }

    private static AttendanceRecordDto Map(AttendanceRecord r, PointIrregularity? irregularidade = null) => new()
    {
        Id = r.Id,
        StudentId = r.StudentId,
        StudentName = r.Student?.FullName ?? string.Empty,
        Type = r.Type,
        RecordedAt = r.RecordedAt,
        Latitude = r.Latitude,
        Longitude = r.Longitude,
        DistanceMeters = r.DistanceMeters,
        PhotoUrl = r.PhotoUrl,
        ActivitiesDescription = r.ActivitiesDescription,
        Status = r.Status,
        IrregularityReason = r.IrregularityReason,
        LocationName = r.Location?.Name,
        ScheduleId = r.ScheduleId,
        LocationId = r.LocationId,
        ValidatedByName = r.ValidatedBy?.FullName,
        ValidatedAt = r.ValidatedAt,
        Shift = Turnos.Normalizar(r.Schedule?.Shift) ?? Turnos.DaHora(r.RecordedAt),
        RemoteActivityId = r.RemoteActivityId,
        RemoteActivityTitle = r.RemoteActivity?.Title,
        IrregularityId = irregularidade?.Id,
        IrregularityStatus = irregularidade?.Status,
        // Negada libera nova contestação; qualquer outra situação mantém o bloqueio.
        HasOpenIrregularity = irregularidade != null
                           && irregularidade.Status != PointIrregularity.StatusNegada
    };
}
