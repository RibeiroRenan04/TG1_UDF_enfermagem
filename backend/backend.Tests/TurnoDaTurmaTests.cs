using EstagioCheck.API.Models;
using Xunit;

namespace EstagioCheck.API.Tests;

/// <summary>
/// O turno exibido ao lado da turma vem dos rodízios dela, e não do cadastro do
/// aluno — o PIC da tarde aparecia como "(Manhã)" para o aluno da manhã.
/// </summary>
public class TurnoDaTurmaTests
{
    private static StudentGroup Turma(params string[] turnos)
    {
        var turma = new StudentGroup { Code = "PIC-T01", Name = "PIC" };
        foreach (var t in turnos)
            turma.Schedules.Add(new RotationSchedule { GroupId = turma.Id, Shift = t });
        return turma;
    }

    [Fact]
    public void Turma_com_rodizios_de_um_turno_so_usa_esse_turno() =>
        Assert.Equal(Turnos.Tarde, TurmasDoAluno.Turno(Turma(Turnos.Tarde, "Tarde")));

    [Fact]
    public void Turma_com_rodizios_em_turnos_diferentes_nao_tem_turno_unico() =>
        Assert.Null(TurmasDoAluno.Turno(Turma(Turnos.Manha, Turnos.Noite)));

    [Fact]
    public void Turma_sem_rodizio_nao_tem_turno() =>
        Assert.Null(TurmasDoAluno.Turno(Turma()));
}
