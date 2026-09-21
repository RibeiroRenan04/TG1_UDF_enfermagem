/** Indicadores da tela de irregularidades — espelha o IrregularidadesPainelDto da API. */
export interface IrregularidadesPainel {
  /** Janela analisada, em dias; nulo é todo o histórico. */
  dias: number | null;

  // Fila atual (não depende do período)
  aguardandoProfessor: number;
  maisAntigaAguardandoProfessorDias: number | null;
  aguardandoPreceptor: number;
  maisAntigaAguardandoPreceptorDias: number | null;

  // Período
  abertasNoPeriodo: number;
  abertasPeriodoAnterior: number | null;
  decididasNoPeriodo: number;
  taxaAprovacao: number | null;
  mediaDiasPreceptor: number | null;
  mediaDiasProfessor: number | null;

  /** Como a evolução foi agrupada. */
  agrupamento: 'semana' | 'mes';
  evolucao: { inicio: string; rotulo: string; abertas: number }[];
  porTipo: { tipo: string; rotulo: string; total: number; aprovadas: number; negadas: number }[];
  porUnidade: { unidadeId: string; nome: string; total: number; foraDoLocal: number }[];
  porAluno: { studentId: string; nome: string; rgm?: string; total: number; negadas: number }[];
  filaPreceptores: { preceptorId: string | null; nome: string; pendentes: number; maisAntigaDias: number }[];
}
