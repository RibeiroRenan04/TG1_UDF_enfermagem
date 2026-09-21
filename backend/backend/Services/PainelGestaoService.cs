using EstagioCheck.API.Data;
using EstagioCheck.API.DTOs;
using EstagioCheck.API.Models;
using Microsoft.EntityFrameworkCore;

namespace EstagioCheck.API.Services;

/// <summary>
/// Indicadores do painel do professor e da coordenadora.
///
/// "Quem deveria estar em estágio" sai da mesma programação que libera o
/// check-in (rodízio, dia da semana, feriados e exceções, data de entrada na
/// turma), carregada em lote para todos os alunos. Assim o painel nunca cobra
/// presença num dia que o próprio ponto dispensaria.
/// </summary>
public class PainelGestaoService(AppDbContext db, ProgramacaoService programacao)
{
    /// <summary>Janela da tendência e do ranking: duas semanas encerradas.</summary>
    public const int DiasDeHistorico = 14;
    private const int LimiteLista = 12;

    public async Task<PainelGestaoDto> MontarAsync(CancellationToken ct = default)
    {
        var hoje = BrasiliaTime.Hoje;
        var inicio = hoje.AddDays(-DiasDeHistorico);

        var alunos = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Aluno && u.IsActive)
            .Select(u => new { u.Id, u.FullName, u.Rgm })
            .ToDictionaryAsync(u => u.Id, ct);
        var ids = alunos.Keys.ToList();

        var turmasPorAluno = (await db.GroupMemberships.AsNoTracking()
                .Where(m => ids.Contains(m.StudentId))
                .Select(m => new { m.StudentId, m.Group.Code })
                .ToListAsync(ct))
            .GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(m => m.Code).OrderBy(c => c)));

        var lote = await programacao.CarregarLoteAsync(ids, inicio, hoje, ct);

        // Check-ins do período, por aluno, data e turno — o turno de um registro é
        // o da escala vinculada; sem escala, o do horário (a mesma regra do ponto).
        var desde = inicio.ToDateTime(TimeOnly.MinValue);
        var registrados = (await db.AttendanceRecords.AsNoTracking()
                .Where(r => r.Type == "check_in" && r.RecordedAt >= desde && ids.Contains(r.StudentId))
                .Select(r => new { r.StudentId, r.RecordedAt, Turno = r.Schedule != null ? r.Schedule.Shift : null })
                .ToListAsync(ct))
            .Select(r => (r.StudentId, DateOnly.FromDateTime(r.RecordedAt),
                          Turnos.Normalizar(r.Turno) ?? Turnos.DaHora(r.RecordedAt)))
            .ToHashSet();

        // ── Turnos esperados × registrados, dia a dia ─────────────────────────
        var dias = new List<PresencaDiaDto>();
        var faltasNoPeriodo = new Dictionary<Guid, int>();
        var porTurnoHoje = Turnos.Validos.ToDictionary(t => t, _ => (Esperados: 0, Registrados: 0));
        var semRegistroHoje = new List<AlunoSemRegistroDto>();

        for (var data = inicio; data <= hoje; data = data.AddDays(1))
        {
            int esperados = 0, feitos = 0;
            foreach (var id in ids)
            {
                foreach (var turno in Turnos.Validos)
                {
                    var dia = lote.NoTurno(id, data, turno);
                    if (dia is not { ExigePonto: true }) continue;

                    esperados++;
                    var registrou = registrados.Contains((id, data, turno));
                    if (registrou) feitos++;

                    if (data == hoje)
                    {
                        var atual = porTurnoHoje[turno];
                        porTurnoHoje[turno] = (atual.Esperados + 1, atual.Registrados + (registrou ? 1 : 0));
                        if (!registrou)
                            semRegistroHoje.Add(new AlunoSemRegistroDto
                            {
                                StudentId = id,
                                Nome = alunos[id].FullName,
                                Rgm = alunos[id].Rgm,
                                Turma = turmasPorAluno.GetValueOrDefault(id),
                                Turno = Turnos.Rotulo(turno),
                                Unidade = dia.Local?.Name ?? ModoAtividade.Rotulo(dia.Modo)
                            });
                    }
                    else if (!registrou)
                    {
                        faltasNoPeriodo[id] = faltasNoPeriodo.GetValueOrDefault(id) + 1;
                    }
                }
            }

            // Hoje fica fora da tendência: o dia ainda não acabou.
            if (data < hoje)
                dias.Add(new PresencaDiaDto { Data = data, Esperados = esperados, Registrados = feitos });
        }

        var hojeDto = new PresencaHojeDto
        {
            Esperados = porTurnoHoje.Values.Sum(t => t.Esperados),
            Registrados = porTurnoHoje.Values.Sum(t => t.Registrados),
            PorTurno = [.. Turnos.Validos
                .Where(t => porTurnoHoje[t].Esperados > 0)
                .Select(t => new PresencaTurnoDto
                {
                    Turno = t,
                    Rotulo = Turnos.Rotulo(t),
                    Esperados = porTurnoHoje[t].Esperados,
                    Registrados = porTurnoHoje[t].Registrados
                })],
            AlunosSemRegistro = [.. semRegistroHoje
                .OrderBy(a => Array.IndexOf(Turnos.Validos, TurnoPorRotulo(a.Turno)))
                .ThenBy(a => a.Nome, StringComparer.CurrentCultureIgnoreCase)
                .Take(LimiteLista)]
        };

        var ranking = faltasNoPeriodo
            .OrderByDescending(f => f.Value)
            .ThenBy(f => alunos[f.Key].FullName, StringComparer.CurrentCultureIgnoreCase)
            .Take(10)
            .Select(f => new AlunoSemRegistroDto
            {
                StudentId = f.Key,
                Nome = alunos[f.Key].FullName,
                Rgm = alunos[f.Key].Rgm,
                Turma = turmasPorAluno.GetValueOrDefault(f.Key),
                Quantidade = f.Value
            })
            .ToList();

        var irregularidades = await db.PointIrregularities.AsNoTracking()
            .Where(i => i.Status == PointIrregularity.StatusAguardandoProfessor
                     || i.Status == PointIrregularity.StatusAguardandoPreceptor)
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Total = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Total, ct);

        return new PainelGestaoDto
        {
            Data = hoje,
            Hoje = hojeDto,
            UltimosDias = dias,
            MaisTurnosSemRegistro = ranking,
            ProgressoCarga = await ProgressoCargaAsync(ids, ct),
            IrregularidadesAguardandoProfessor = irregularidades.GetValueOrDefault(PointIrregularity.StatusAguardandoProfessor),
            IrregularidadesAguardandoPreceptor = irregularidades.GetValueOrDefault(PointIrregularity.StatusAguardandoPreceptor),
            Configuracao = await AlertasConfiguracaoAsync(hoje, ct)
        };
    }

    private static string TurnoPorRotulo(string? rotulo) =>
        Turnos.Validos.FirstOrDefault(t => Turnos.Rotulo(t) == rotulo) ?? Turnos.Manha;

    // ── Carga horária ─────────────────────────────────────────────────────────
    /// <summary>
    /// Alunos por faixa de carga horária cumprida. Horas e carga exigida seguem a
    /// mesma conta do certificado: pares de check-in/check-out aprovados, e a
    /// soma dos rodízios de todas as turmas do aluno.
    /// </summary>
    private async Task<ProgressoCargaDto> ProgressoCargaAsync(List<Guid> ids, CancellationToken ct)
    {
        var exigidas = (await db.GroupMemberships.AsNoTracking()
                .Where(m => ids.Contains(m.StudentId))
                .Select(m => new { m.StudentId, Horas = m.Group.Schedules.Sum(s => s.RequiredHours) })
                .ToListAsync(ct))
            .GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Horas));

        var registrosPorAluno = (await db.AttendanceRecords.AsNoTracking()
                .Where(r => ids.Contains(r.StudentId))
                .Select(r => new { r.StudentId, r.Type, r.Status, r.RecordedAt })
                .ToListAsync(ct))
            .GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(r =>
                new CertificateService.RegistroHora(r.Type, r.Status, r.RecordedAt)).ToList());

        string[] rotulos = ["Até 25%", "25% a 50%", "50% a 75%", "75% a 99%", "Concluída"];
        var faixas = new int[rotulos.Length];
        int semCarga = 0, elegiveis = 0;

        foreach (var id in ids)
        {
            var exigida = exigidas.GetValueOrDefault(id);
            if (exigida <= 0) { semCarga++; continue; }

            var horas = CertificateService.CalcularHorasAprovadas(registrosPorAluno.GetValueOrDefault(id) ?? []);
            var pct = 100 * horas / exigida;

            if (pct >= 100) { faixas[4]++; elegiveis++; }
            else if (pct >= 75) faixas[3]++;
            else if (pct >= 50) faixas[2]++;
            else if (pct >= 25) faixas[1]++;
            else faixas[0]++;
        }

        return new ProgressoCargaDto
        {
            Faixas = [.. rotulos.Select((r, i) => new FaixaProgressoDto { Rotulo = r, Alunos = faixas[i] })],
            Elegiveis = elegiveis,
            SemCargaDefinida = semCarga
        };
    }

    // ── Pendências de configuração ────────────────────────────────────────────
    /// <summary>
    /// O que impede o estágio de acontecer direito. Cada item aponta a tela onde
    /// se resolve — é a lista que evitou, por exemplo, 327 unidades sem localização
    /// passarem despercebidas.
    /// </summary>
    private async Task<List<AlertaConfiguracaoDto>> AlertasConfiguracaoAsync(DateOnly hoje, CancellationToken ct)
    {
        var alertas = new List<AlertaConfiguracaoDto>();

        var alunosSemTurma = await db.Users.CountAsync(u =>
            u.Role == Roles.Aluno && u.IsActive && !u.GroupMemberships.Any(), ct);

        var turmasSemAlunos = await db.StudentGroups.CountAsync(g => !g.Memberships.Any(), ct);

        // Turma com alunos e nenhum rodízio de hoje em diante: ninguém dela tem
        // programação, então ninguém consegue registrar ponto.
        var turmasSemRodizio = await db.StudentGroups.CountAsync(g =>
            g.Memberships.Any() && !g.Schedules.Any(s => s.EndDate >= hoje), ct);

        var rodiziosAtivos = db.RotationSchedules.Where(s => s.EndDate >= hoje);

        var rodiziosSemPreceptor = await rodiziosAtivos.CountAsync(s => s.PreceptorId == null, ct);

        // Localização confirmada = coordenada fora de (0, 0) e geocodificação com
        // sucesso (ou cadastro antigo sem status) — a mesma regra do check-in.
        var semLocalizacao = db.Locations.Where(l =>
            (l.Latitude == 0 && l.Longitude == 0)
            || (l.StatusGeocodificacao != null && l.StatusGeocodificacao != StatusGeocodificacao.Sucesso));

        var rodiziosEmUnidadeSemLocalizacao = await rodiziosAtivos.CountAsync(s =>
            semLocalizacao.Any(l => l.Id == s.LocationId)
            || s.Days.Any(d => d.LocationId != null && semLocalizacao.Any(l => l.Id == d.LocationId)), ct);

        var unidadesSemLocalizacao = await semLocalizacao.CountAsync(l => l.Ativo, ct);

        alertas.Add(new AlertaConfiguracaoDto
        {
            Codigo = "rodizio_unidade_sem_localizacao",
            Titulo = "Rodízios em unidade sem localização confirmada",
            Detalhe = "O check-in é recusado nessas unidades até a localização ser confirmada.",
            Quantidade = rodiziosEmUnidadeSemLocalizacao,
            Severidade = "critico",
            Link = "/app/unidades/revisao",
            LinkRotulo = "Revisar localizações"
        });
        alertas.Add(new AlertaConfiguracaoDto
        {
            Codigo = "turma_sem_rodizio",
            Titulo = "Turmas com alunos e sem rodízio",
            Detalhe = "Sem rodízio vigente ou futuro, os alunos da turma não têm onde registrar ponto.",
            Quantidade = turmasSemRodizio,
            Severidade = "critico",
            Link = "/app/rodizios",
            LinkRotulo = "Alocar rodízio"
        });
        alertas.Add(new AlertaConfiguracaoDto
        {
            Codigo = "aluno_sem_turma",
            Titulo = "Alunos ativos sem turma",
            Detalhe = "Sem turma, o aluno não recebe rodízio nem consegue fazer check-in.",
            Quantidade = alunosSemTurma,
            Severidade = "atencao",
            Link = "/app/rodizios",
            LinkRotulo = "Vincular alunos"
        });
        alertas.Add(new AlertaConfiguracaoDto
        {
            Codigo = "rodizio_sem_preceptor",
            Titulo = "Rodízios sem preceptor",
            Detalhe = "Sem preceptor, ninguém acompanha os alunos nem analisa as irregularidades do rodízio.",
            Quantidade = rodiziosSemPreceptor,
            Severidade = "atencao",
            Link = "/app/rodizios",
            LinkRotulo = "Ver rodízios"
        });
        alertas.Add(new AlertaConfiguracaoDto
        {
            Codigo = "unidade_sem_localizacao",
            Titulo = "Unidades sem localização confirmada",
            Detalhe = "Ainda não podem receber rodízio com check-in.",
            Quantidade = unidadesSemLocalizacao,
            Severidade = "atencao",
            Link = "/app/unidades/revisao",
            LinkRotulo = "Revisar localizações"
        });
        alertas.Add(new AlertaConfiguracaoDto
        {
            Codigo = "turma_sem_alunos",
            Titulo = "Turmas sem alunos",
            Detalhe = "Turma vazia não pode receber rodízio.",
            Quantidade = turmasSemAlunos,
            Severidade = "atencao",
            Link = "/app/rodizios",
            LinkRotulo = "Vincular alunos"
        });

        return alertas;
    }
}
