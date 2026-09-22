const TURNOS: Record<string, string> = { manha: 'Manhã', tarde: 'Tarde', noite: 'Noite' };

export function rotuloTurno(turno?: string | null): string {
  return turno ? (TURNOS[turno] ?? turno) : '';
}

/**
 * Turno a exibir ao lado da turma: o da própria turma, que vem dos rodízios dela.
 * O turno cadastrado do aluno só entra quando a API não mandou o da turma (sessão
 * antiga) — usá-lo sempre fazia o PIC da tarde aparecer como "(Manhã)".
 */
export function turnoDaTurma(turma: { shift?: string | null }, turnoDoAluno?: string | null): string | null {
  return turma.shift !== undefined ? turma.shift : (turnoDoAluno ?? null);
}

/** Identificação da turma do aluno: "T02 - Teste (Manhã)". Vazio sem turma. */
export function rotuloTurma(codigo?: string | null, nome?: string | null, turno?: string | null): string {
  if (!codigo) return '';
  const base = nome && nome !== codigo ? `${codigo} - ${nome}` : codigo;
  const t = rotuloTurno(turno);
  return t ? `${base} (${t})` : base;
}
