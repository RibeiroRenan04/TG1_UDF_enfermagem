/** Painel do professor e da coordenadora — espelha o PainelGestaoDto da API. */
export interface PainelGestao {
  data: string;
  hoje: PresencaHoje;
  /** Presença dos últimos 14 dias encerrados (até ontem). */
  ultimosDias: PresencaDia[];
  /** Alunos com mais turnos sem registro no mesmo período. */
  maisTurnosSemRegistro: AlunoSemRegistro[];
  progressoCarga: ProgressoCarga;
  irregularidadesAguardandoProfessor: number;
  irregularidadesAguardandoPreceptor: number;
  configuracao: AlertaConfiguracao[];
}

export interface PresencaHoje {
  esperados: number;
  registrados: number;
  semRegistro: number;
  porTurno: PresencaTurno[];
  alunosSemRegistro: AlunoSemRegistro[];
}

export interface PresencaTurno {
  turno: string;
  rotulo: string;
  esperados: number;
  registrados: number;
}

export interface PresencaDia {
  data: string;
  esperados: number;
  registrados: number;
  /** Nulo quando ninguém tinha estágio no dia (fim de semana, feriado). */
  percentual: number | null;
}

export interface AlunoSemRegistro {
  studentId: string;
  nome: string;
  rgm?: string;
  turma?: string;
  turno?: string;
  unidade?: string;
  quantidade: number;
}

export interface ProgressoCarga {
  faixas: { rotulo: string; alunos: number }[];
  elegiveis: number;
  semCargaDefinida: number;
}

export interface AlertaConfiguracao {
  codigo: string;
  titulo: string;
  detalhe: string;
  quantidade: number;
  severidade: 'critico' | 'atencao';
  link: string;
  linkRotulo: string;
}
