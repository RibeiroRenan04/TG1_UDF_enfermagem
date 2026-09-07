import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { StudentGroup, RotationSchedule, GroupMember, ModoAtividade } from '../models/models';

/** Regra de um dia da semana enviada junto com o rodízio. */
export interface DiaRodizioInput {
  dayOfWeek: number;
  mode: ModoAtividade;
  /** Ausente herda o local principal do rodízio. */
  locationId?: string;
  notes?: string;
}

/**
 * Dados da alocação de rodízio. `days` é a programação semanal: informada, o
 * sistema gera sozinho a programação de cada data do período; omitida ou vazia,
 * o rodízio segue no padrão de dias úteis presenciais no local principal.
 */
export interface RotationScheduleInput {
  groupId: string;
  locationId: string;
  preceptorId?: string;
  shift: string;
  periodLabel: string;
  startDate: string;
  endDate: string;
  activityType: string;
  requiredHours: number;
  notes?: string;
  days?: DiaRodizioInput[];
}

@Injectable({ providedIn: 'root' })
export class GroupsService {
  private readonly api = `${environment.apiUrl}/groups`;
  constructor(private http: HttpClient) {}

  getAll(): Observable<StudentGroup[]> {
    return this.http.get<StudentGroup[]>(this.api);
  }

  create(dto: { code: string; name: string; description?: string }): Observable<StudentGroup> {
    return this.http.post<StudentGroup>(this.api, dto);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/${id}`);
  }

  /** Alunos vinculados à turma — conferência antes da alocação do rodízio. */
  getMembers(groupId: string): Observable<GroupMember[]> {
    return this.http.get<GroupMember[]>(`${this.api}/${groupId}/members`);
  }

  getSchedules(groupId?: string): Observable<RotationSchedule[]> {
    const url = groupId ? `${this.api}/${groupId}/schedules` : `${this.api}/schedules`;
    return this.http.get<RotationSchedule[]>(url);
  }

  createSchedule(dto: RotationScheduleInput): Observable<RotationSchedule> {
    return this.http.post<RotationSchedule>(`${this.api}/schedules`, dto);
  }

  updateSchedule(id: string, dto: RotationScheduleInput): Observable<RotationSchedule> {
    return this.http.put<RotationSchedule>(`${this.api}/schedules/${id}`, dto);
  }

  deleteSchedule(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/schedules/${id}`);
  }
}
