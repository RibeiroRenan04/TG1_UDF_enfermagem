const TURNOS: Record<string, string> = { manha: 'Manhã', tarde: 'Tarde', noite: 'Noite' };

export function rotuloTurno(turno?: string | null): string {
  return turno ? (TURNOS[turno] ?? turno) : '';
}

/** Identificação da turma do aluno: "T02 - Teste (Manhã)". Vazio sem turma. */
export function rotuloTurma(codigo?: string | null, nome?: string | null, turno?: string | null): string {
  if (!codigo) return '';
  const base = nome && nome !== codigo ? `${codigo} - ${nome}` : codigo;
  const t = rotuloTurno(turno);
  return t ? `${base} (${t})` : base;
}
