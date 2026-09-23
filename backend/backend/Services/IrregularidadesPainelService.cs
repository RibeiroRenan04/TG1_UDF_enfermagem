using System.Globalization;
using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services;

/// <summary>A fila é sempre a atual; os padrões olham a janela escolhida. Tudo é agregado em memória.</summary>
public class IrregularidadesPainelService(AppDbContext db)
{
    public static readonly IReadOnlyDictionary<string, string> RotulosTipo = new Dictionary<string, string>
    {
        ["atraso"] = "Atraso",
        ["esquecimento_checkin"] = "Esqueceu o check-in",
        ["esquecimento_checkout"] = "Esqueceu o check-out",
        ["fora_do_local"] = "Registro fora do local",
        ["falta_justificada"] = "Falta justificada",
        ["problema_tecnico"] = "Problema técnico",
        ["outro"] = "Outro"
    };

    public async Task<IrregularidadesPainelDto> MontarAsync(int? dias, CancellationToken ct = default)
    {
        var agora = BrasiliaTime.Agora;
        var inicio = dias.HasValue ? agora.Date.AddDays(-dias.Value + 1) : (DateTime?)null;
        var inicioAnterior = dias.HasValue ? inicio!.Value.AddDays(-dias.Value) : (DateTime?)null;

        var todas = await db.PointIrregularities.AsNoTracking()
            .Select(i => new
            {
                i.Id, i.StudentId, i.Type, i.Status, i.CreatedAt,
                i.PreceptorAcknowledgedAt, i.ProfessorDecidedAt,
                Aluno = i.Student.FullName, i.Student.Rgm,
                // Unidade: a do ponto contestado; sem ponto, a do rodízio.
                UnidadeId = i.AttendanceRecord != null && i.AttendanceRecord.LocationId != null
                    ? i.AttendanceRecord.LocationId
                    : i.Schedule != null ? (Guid?)i.Schedule.LocationId : null,
                // Quem deveria dar ciência: o preceptor do rodízio da ocorrência.
                PreceptorId = i.Schedule != null ? i.Schedule.PreceptorId
                    : i.AttendanceRecord != null && i.AttendanceRecord.Schedule != null
                        ? i.AttendanceRecord.Schedule.PreceptorId : null
            })
            .ToListAsync(ct);

        int DiasDesde(DateTime d) => Math.Max(0, (int)(agora - d).TotalDays);

        var comProfessor = todas.Where(i => i.Status == PointIrregularity.StatusAguardandoProfessor).ToList();
        var comPreceptor = todas.Where(i => i.Status == PointIrregularity.StatusAguardandoPreceptor).ToList();

        var nomesPreceptores = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Preceptor)
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var fila = comPreceptor
            .GroupBy(i => i.PreceptorId)
            .Select(g => new FilaPreceptorDto
            {
                PreceptorId = g.Key,
                Nome = g.Key.HasValue && nomesPreceptores.TryGetValue(g.Key.Value, out var nome)
                    ? nome : "Sem preceptor no rodízio",
                Pendentes = g.Count(),
                MaisAntigaDias = g.Max(i => DiasDesde(i.CreatedAt))
            })
            .OrderByDescending(f => f.MaisAntigaDias)
            .ThenByDescending(f => f.Pendentes)
            .ToList();

        var abertas = todas.Where(i => inicio == null || i.CreatedAt >= inicio).ToList();
        var decididas = todas
            .Where(i => i.ProfessorDecidedAt != null && (inicio == null || i.ProfessorDecidedAt >= inicio)
                     && (i.Status == PointIrregularity.StatusAprovada || i.Status == PointIrregularity.StatusNegada))
            .ToList();

        double? Media(IEnumerable<double> valores)
        {
            var lista = valores.ToList();
            return lista.Count == 0 ? null : Math.Round(lista.Average(), 1);
        }

        var mediaPreceptor = Media(todas
            .Where(i => i.PreceptorAcknowledgedAt != null && (inicio == null || i.PreceptorAcknowledgedAt >= inicio))
            .Select(i => (i.PreceptorAcknowledgedAt!.Value - i.CreatedAt).TotalDays));

        // O professor pode decidir antes da ciência: aí o tempo dele conta da abertura.
        var mediaProfessor = Media(decididas
            .Select(i => (i.ProfessorDecidedAt!.Value - (i.PreceptorAcknowledgedAt ?? i.CreatedAt)).TotalDays));

        var aprovadas = decididas.Count(i => i.Status == PointIrregularity.StatusAprovada);
        var evolucao = MontarEvolucao(abertas.Select(i => i.CreatedAt).ToList(), inicio, agora);

        return new IrregularidadesPainelDto
        {
            Dias = dias,
            AguardandoProfessor = comProfessor.Count,
            MaisAntigaAguardandoProfessorDias = comProfessor.Count == 0 ? null
                : comProfessor.Max(i => DiasDesde(i.PreceptorAcknowledgedAt ?? i.CreatedAt)),
            AguardandoPreceptor = comPreceptor.Count,
            MaisAntigaAguardandoPreceptorDias = comPreceptor.Count == 0 ? null
                : comPreceptor.Max(i => DiasDesde(i.CreatedAt)),

            AbertasNoPeriodo = abertas.Count,
            AbertasPeriodoAnterior = inicioAnterior == null ? null
                : todas.Count(i => i.CreatedAt >= inicioAnterior && i.CreatedAt < inicio),
            DecididasNoPeriodo = decididas.Count,
            TaxaAprovacao = decididas.Count == 0 ? null : Math.Round(100.0 * aprovadas / decididas.Count, 1),
            MediaDiasPreceptor = mediaPreceptor,
            MediaDiasProfessor = mediaProfessor,

            Agrupamento = evolucao.Semanal ? "semana" : "mes",
            Evolucao = evolucao.Baldes,

            PorTipo = [.. abertas
                .GroupBy(i => i.Type)
                .Select(g => new IrregularidadesPorTipoDto
                {
                    Tipo = g.Key,
                    Rotulo = RotulosTipo.GetValueOrDefault(g.Key, g.Key),
                    Total = g.Count(),
                    Aprovadas = g.Count(i => i.Status == PointIrregularity.StatusAprovada),
                    Negadas = g.Count(i => i.Status == PointIrregularity.StatusNegada)
                })
                .OrderByDescending(t => t.Total)],

            PorUnidade = await PorUnidadeAsync(abertas
                .Where(i => i.UnidadeId != null)
                .Select(i => (i.UnidadeId!.Value, i.Type)).ToList(), ct),

            PorAluno = [.. abertas
                .GroupBy(i => i.StudentId)
                .Where(g => g.Count() >= 2)   // uma ocorrência isolada não é padrão
                .Select(g => new IrregularidadesPorAlunoDto
                {
                    StudentId = g.Key,
                    Nome = g.First().Aluno,
                    Rgm = g.First().Rgm,
                    Total = g.Count(),
                    Negadas = g.Count(i => i.Status == PointIrregularity.StatusNegada)
                })
                .OrderByDescending(a => a.Total)
                .ThenBy(a => a.Nome, StringComparer.CurrentCultureIgnoreCase)
                .Take(8)],

            FilaPreceptores = fila
        };
    }

    private async Task<List<IrregularidadesPorUnidadeDto>> PorUnidadeAsync(
        List<(Guid UnidadeId, string Tipo)> ocorrencias, CancellationToken ct)
    {
        if (ocorrencias.Count == 0) return [];

        var ids = ocorrencias.Select(o => o.UnidadeId).Distinct().ToList();
        var nomes = await db.Locations.AsNoTracking()
            .Where(l => ids.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, ct);

        return [.. ocorrencias
            .GroupBy(o => o.UnidadeId)
            .Select(g => new IrregularidadesPorUnidadeDto
            {
                UnidadeId = g.Key,
                Nome = nomes.GetValueOrDefault(g.Key, "Unidade removida"),
                Total = g.Count(),
                ForaDoLocal = g.Count(o => o.Tipo == "fora_do_local")
            })
            .OrderByDescending(u => u.Total)
            .ThenByDescending(u => u.ForaDoLocal)
            .Take(8)];
    }

    /// <summary>Por semana até 90 dias; por mês acima disso, para o gráfico não virar um pente de barras.</summary>
    private static (bool Semanal, List<IrregularidadesPeriodoDto> Baldes) MontarEvolucao(
        List<DateTime> criadas, DateTime? inicio, DateTime agora)
    {
        var de = DateOnly.FromDateTime(inicio ?? (criadas.Count > 0 ? criadas.Min() : agora));
        var ate = DateOnly.FromDateTime(agora);
        var semanal = (ate.DayNumber - de.DayNumber) <= 91;
        var cultura = CultureInfo.GetCultureInfo("pt-BR");

        var baldes = new List<IrregularidadesPeriodoDto>();
        if (semanal)
        {
            var cursor = de.AddDays(-(((int)de.DayOfWeek + 6) % 7));
            while (cursor <= ate)
            {
                var fim = cursor.AddDays(7);
                baldes.Add(new IrregularidadesPeriodoDto
                {
                    Inicio = cursor,
                    Rotulo = cursor.ToString("dd/MM", cultura),
                    Abertas = criadas.Count(c => DateOnly.FromDateTime(c) >= cursor && DateOnly.FromDateTime(c) < fim)
                });
                cursor = fim;
            }
        }
        else
        {
            var cursor = new DateOnly(de.Year, de.Month, 1);
            while (cursor <= ate)
            {
                var fim = cursor.AddMonths(1);
                baldes.Add(new IrregularidadesPeriodoDto
                {
                    Inicio = cursor,
                    Rotulo = cursor.ToString("MMM/yy", cultura),
                    Abertas = criadas.Count(c => DateOnly.FromDateTime(c) >= cursor && DateOnly.FromDateTime(c) < fim)
                });
                cursor = fim;
            }
        }

        return (semanal, baldes);
    }
}
