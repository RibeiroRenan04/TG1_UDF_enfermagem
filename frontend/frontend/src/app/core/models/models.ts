/** Perfis de acesso. "supervisor" é o professor responsável. */
export type UserRole = 'aluno' | 'preceptor' | 'supervisor' | 'coordenadora';

export interface AuthResponse {
  token: string;
  userId: string;
  email: string;
  fullName: string;
  role: UserRole;
  mustChangePassword?: boolean;
  mustSetEmail?: boolean;
  /** Perfis não-aluno precisam aceitar o termo de responsabilidade de acesso. */
  mustAcceptTerms?: boolean;
}

/** Termo de responsabilidade exibido a preceptores, professores e coordenadoras. */
export interface ResponsibilityTerms {
  titulo: string;
  versao: string;
  itens: string[];
}

export interface RegisterDto {
  fullName: string;
  email: string;
  password: string;
  matricula?: string;
  role: string;
}

export interface LoginDto {
  email: string;
  password: string;
}

export interface Location {
  id: string;
  name: string;
  address?: string;
  latitude: number;
  longitude: number;
  radiusMeters: number;
  isInstitution: boolean;
  shiftStart: string;
  shiftEnd: string;
  codigoCnes?: string;
}

/** Estabelecimento de saúde retornado pela busca no CNES (Busca Saúde DF). */
export interface BuscaSaudeEstabelecimento {
  codigoCnes: string;
  nome: string;
  endereco: string;
  latitude: number;
  longitude: number;
  telefone?: string;
  turnoAtendimento?: string;
}

export interface StudentGroup {
  id: string;
  code: string;
  name: string;
  description?: string;
  memberCount: number;
}

export interface RotationSchedule {
  id: string;
  groupId: string;
  groupCode: string;
  groupName?: string;
  locationId: string;
  locationName: string;
  preceptorId?: string;
  preceptorName?: string;
  shift: string;
  periodLabel: string;
  startDate: string;
  endDate: string;
  activityType: string;
  requiredHours: number;
  notes?: string;
}

export interface AttendanceRecord {
  id: string;
  studentId: string;
  studentName: string;
  type: 'check_in' | 'check_out';
  recordedAt: string;
  latitude: number;
  longitude: number;
  distanceMeters?: number;
  photoUrl?: string;
  activitiesDescription?: string;
  status: 'aprovado' | 'irregular' | 'pendente';
  irregularityReason?: string;
  locationName?: string;
  scheduleId?: string;
  locationId?: string;
  validatedByName?: string;
  validatedAt?: string;
}

export interface ActiveSchedule {
  scheduleId: string;
  shift: string;
  periodLabel: string;
  activityType: string;
  requiredHours: number;
  location: Location;
}

export interface Pendency {
  pendencyDate: string;
  scheduleId?: string;
  locationName: string;
  expectedHours: number;
}

export interface DashboardStats {
  total: number;
  approved: number;
  irregular: number;
  pending: number;
  hours: number;
  required: number;
  pendencyDays: number;
  pendencyHours: number;
  totalStudents?: number;
  pendencies: Pendency[];
}

export interface UserDto {
  id: string;
  fullName: string;
  email: string;
  rgm?: string;
  role: string;
  groupId?: string;
  groupCode?: string;
  groupName?: string;
  semester?: 7 | 8;
  shift?: 'manha' | 'tarde' | 'noite';
  mustChangePassword?: boolean;
  mustSetEmail?: boolean;
  isActive?: boolean;
  /** Aluno autorizado a chegar após o horário previsto de início do estágio. */
  allowLateArrival?: boolean;
  /** Motivo da autorização de atraso, registrado pelo professor. */
  lateArrivalNote?: string;
  /** Quando o usuário aceitou o termo de responsabilidade de acesso. */
  termsAcceptedAt?: string;
}

export interface SemesterHistory {
  id: string;
  userId: string;
  semester: number;
  shift: string;
  startDate: string;
  endDate?: string;
  totalHours: number;
}

export interface BulkImportStudent {
  rgm: string;
  fullName: string;
  semester: 7 | 8;
  shift: 'manha' | 'tarde' | 'noite';
}

/** Login gerado para um aluno na importação. A senha inicial é o RGM. */
export interface ImportedStudentLogin {
  fullName: string;
  rgm: string;
  email: string;
}

export interface BulkImportResult {
  imported: number;
  updated: number;
  /** O backend devolve mensagens já formatadas ("RGM 123: motivo"). */
  errors: string[];
  logins: ImportedStudentLogin[];
}

export interface AdvanceSemesterResult {
  advanced: number;
  graduated: number;
}

export interface ReportRow {
  studentId: string;
  fullName: string;
  required: number;
  hours: number;
  approved: number;
  irregular: number;
  pendencyDays: number;
  pendencyHours: number;
  progressPercent: number;
  certificateReleased: boolean;
}

export interface StudentLookup {
  studentId: string;
  fullName: string;
  rgm?: string;
  /** Dados já cadastrados, usados no preenchimento automático do relatório. */
  semester?: number;
  shift?: string;
  periodLabel?: string;
  groupId?: string;
  groupCode?: string;
  groupName?: string;
  scheduleId?: string;
  locationId?: string;
  locationName?: string;
  activityType?: string;
  followUpStart?: string;
  followUpEnd?: string;
}

export interface Certificate {
  studentId: string;
  studentName: string;
  rgm?: string;
  groupName?: string;
  completedHours: number;
  requiredHours: number;
  progressPercent: number;
  eligible: boolean;
  periodLabel?: string;
  locations: string[];
  institution?: string;
  issuedAt: string;
  verificationCode: string;
}

export interface Evaluation {
  id: string;
  studentId: string;
  studentName: string;
  preceptorId: string;
  preceptorName: string;
  activitiesScore: number;
  postureScore: number;
  planningScore: number;
  comment?: string;
  createdAt: string;
}

export interface FormativeFollowup {
  id: string;
  studentId: string;
  studentName: string;
  studentRgm?: string;
  preceptorId: string;
  preceptorName: string;
  scheduleId?: string;
  groupId?: string;
  locationId?: string;
  locationName?: string;
  shift?: string;
  periodLabel?: string;
  semester?: string;
  followUpStart?: string;
  followUpEnd?: string;
  posturaPontualidade?: string;
  posturaEtica?: string;
  posturaResponsabilidade?: string;
  comunicacaoEquipe?: string;
  comunicacaoPaciente?: string;
  comunicacaoEscuta?: string;
  organizacaoPlanejamento?: string;
  organizacaoSeguranca?: string;
  organizacaoRegistros?: string;
  participacaoIniciativa?: string;
  participacaoAprendizado?: string;
  participacaoAutocritica?: string;
  potencialidades?: string;
  aspectosAprimorar?: string;
  situacoesRelevantes?: string;
  observacoesDocente?: string;
  evolucaoSemanal?: string;
  status: 'rascunho' | 'finalizado_preceptor' | 'finalizado_aluno';
  preceptorSignedAt?: string;
  preceptorSignedName?: string;
  studentSignedAt?: string;
  studentSignedName?: string;
  createdAt: string;
  updatedAt: string;
}

/** Aluno vinculado a uma turma (conferência antes da alocação do rodízio). */
export interface GroupMember {
  studentId: string;
  fullName: string;
  rgm?: string;
  semester?: number;
  shift?: string;
  isActive: boolean;
}

// ── Irregularidades de ponto ─────────────────────────────────────────────────
/**
 * Fluxo: o aluno registra (ou o sistema gera) → o preceptor toma ciência e pode
 * observar → a ocorrência vai ao professor → o professor aprova ou nega.
 * O preceptor nunca decide a situação.
 */
export type IrregularityStatus =
  | 'aguardando_preceptor'
  | 'aguardando_professor'
  | 'aprovada'
  | 'negada';

export type IrregularityType =
  | 'atraso'
  | 'esquecimento_checkin'
  | 'esquecimento_checkout'
  | 'fora_do_local'
  | 'falta_justificada'
  | 'problema_tecnico'
  | 'outro';

export interface Irregularity {
  id: string;
  studentId: string;
  studentName: string;
  studentRgm?: string;
  attendanceRecordId?: string;
  scheduleId?: string;
  type: IrregularityType;
  occurredOn: string;
  description: string;
  status: IrregularityStatus;

  preceptorId?: string;
  preceptorName?: string;
  preceptorNote?: string;
  preceptorAcknowledgedAt?: string;

  professorId?: string;
  professorName?: string;
  professorNote?: string;
  professorDecidedAt?: string;

  createdAt: string;
  updatedAt: string;
}

export interface IrregularitySummary {
  aguardandoPreceptor: number;
  aguardandoProfessor: number;
  aprovadas: number;
  negadas: number;
  total: number;
}

export interface CreateIrregularity {
  type: IrregularityType;
  occurredOn: string;
  description: string;
  attendanceRecordId?: string;
  scheduleId?: string;
}

// ── Rodízios do preceptor com os alunos alocados ─────────────────────────────
/**
 * Um rodízio e os alunos alocados nele. Cada aluno já vem com o contexto do
 * rodízio (período, turno, local e datas), então escolher um da lista preenche
 * o acompanhamento inteiro.
 */
export interface ScheduleStudents {
  scheduleId: string;
  periodLabel: string;
  shift: string;
  activityType: string;
  groupId?: string;
  groupCode?: string;
  groupName?: string;
  locationId?: string;
  locationName?: string;
  startDate: string;
  endDate: string;
  /** Rodízio vigente hoje. */
  current: boolean;
  students: StudentLookup[];
}

// ── Unidades de saúde ────────────────────────────────────────────────────────
/** Situação da geocodificação de uma unidade. */
export type StatusGeocodificacao =
  | 'pendente' | 'processando' | 'sucesso'
  | 'nao_encontrado' | 'erro' | 'revisao_manual';

/** De onde vieram as coordenadas da unidade. */
export type OrigemCoordenadas = 'NOMINATIM' | 'MANUAL' | 'OUTRO';

export interface UnidadeSaude {
  id: string;
  nome: string;
  tipo?: string;
  endereco?: string;
  numero?: string;
  complemento?: string;
  bairro?: string;
  cidade?: string;
  uf?: string;
  cep?: string;
  telefone?: string;
  enderecoCompleto: string;

  latitude: number;
  longitude: number;
  temCoordenadas: boolean;
  raioMetros: number;
  origemCoordenadas?: OrigemCoordenadas;
  statusGeocodificacao?: StatusGeocodificacao;
  enderecoGeocodificado?: string;
  precisaoLocalizacao?: string;
  geocodificadoEm?: string;

  ehInstituicao: boolean;
  inicioTurno?: string;
  fimTurno?: string;
  codigoCnes?: string;
  ativo: boolean;

  /** Estagiários com alocação ativa nesta unidade. */
  estagiariosAtivos: number;
  criadoEm: string;
  atualizadoEm: string;
}

export interface CriarUnidadeSaude {
  nome: string;
  tipo?: string;
  endereco?: string;
  numero?: string;
  complemento?: string;
  bairro?: string;
  cidade?: string;
  uf?: string;
  cep?: string;
  telefone?: string;
  latitude?: number;
  longitude?: number;
  raioMetros?: number;
  ehInstituicao?: boolean;
  inicioTurno?: string;
  fimTurno?: string;
  geocodificarAgora?: boolean;
}

export interface GeocodificacaoResposta {
  sucesso: boolean;
  status: StatusGeocodificacao;
  latitude?: number;
  longitude?: number;
  enderecoEncontrado?: string;
  precisao?: string;
  mensagem?: string;
  veioDoCache: boolean;
}

// ── Importação de unidades ───────────────────────────────────────────────────
export interface ImportPreviewLinha {
  linha: number;
  nome: string;
  tipo?: string;
  enderecoResumo: string;
  cidade?: string;
  cep?: string;
  /** "valida" | "invalida" | "duplicada" | "duplicada_endereco_alterado" */
  status: string;
  erros: string[];
  unidadeExistenteId?: string;
}

export interface ImportPreview {
  previewId: string;
  totalLinhas: number;
  validas: number;
  invalidas: number;
  duplicadas: number;
  erros: string[];
  linhas: ImportPreviewLinha[];
  podeConfirmar: boolean;
}

export interface ImportacaoResultado {
  loteId: string;
  criadas: number;
  atualizadas: number;
  ignoradas: number;
  enfileiradasParaGeocodificar: number;
  mensagem: string;
}

export interface ImportacaoProgresso {
  loteId: string;
  total: number;
  processados: number;
  pendentes: number;
  sucesso: number;
  revisaoManual: number;
  naoEncontrado: number;
  erro: number;
  percentualConcluido: number;
  concluido: boolean;
}

// ── Alocação de estagiários ──────────────────────────────────────────────────
export interface Alocacao {
  id: string;
  unidadeId: string;
  unidadeNome: string;
  unidadeCidade?: string;
  estagiarioId: string;
  estagiarioNome: string;
  estagiarioRgm?: string;
  estagiarioEmail?: string;
  estagiarioSemestre?: number;
  estagiarioTurno?: string;
  dataInicio: string;
  dataFim?: string;
  ativo: boolean;
  observacao?: string;
  criadoPorNome?: string;
  criadoEm: string;
}

export interface EstagiarioDisponivel {
  id: string;
  nome: string;
  rgm?: string;
  email?: string;
  semestre?: number;
  turno?: string;
  turma?: string;
  /** Unidade em que já está alocado, se houver. */
  unidadeAtualId?: string;
  unidadeAtualNome?: string;
}
