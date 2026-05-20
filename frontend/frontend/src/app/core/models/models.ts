export interface AuthResponse {
  token: string;
  userId: string;
  email: string;
  fullName: string;
  role: 'aluno' | 'preceptor' | 'supervisor';
  mustChangePassword?: boolean;
  mustSetEmail?: boolean;
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
  matricula?: string;
  role: string;
  groupId?: string;
  groupCode?: string;
  groupName?: string;
  semester?: 7 | 8;
  shift?: 'manha' | 'tarde' | 'noite';
  mustChangePassword?: boolean;
  mustSetEmail?: boolean;
  isActive?: boolean;
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

export interface BulkImportResult {
  imported: number;
  updated: number;
  errors: { rgm: string; reason: string }[];
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
  posturapontualidade?: string;
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
