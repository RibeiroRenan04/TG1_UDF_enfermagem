import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { FormativeFollowup, StudentLookup, ScheduleStudents } from '../models/models';

@Injectable({ providedIn: 'root' })
export class FollowupsService {
  private readonly api = `${environment.apiUrl}/followups`;
  constructor(private http: HttpClient) {}

  getAll(): Observable<FormativeFollowup[]> {
    return this.http.get<FormativeFollowup[]>(this.api);
  }

  /**
   * Rodízios do preceptor com os alunos alocados em cada um — usado para
   * escolher o aluno pela lista da turma em vez de digitar o RGM.
   */
  getMySchedules(): Observable<ScheduleStudents[]> {
    return this.http.get<ScheduleStudents[]>(`${this.api}/my-schedules`);
  }

  /** Busca um aluno pelo RGM (nome + id) para preencher o acompanhamento. */
  getStudentByRgm(rgm: string): Observable<StudentLookup> {
    return this.http.get<StudentLookup>(`${this.api}/student-by-rgm/${encodeURIComponent(rgm)}`);
  }

  get(id: string): Observable<FormativeFollowup> {
    return this.http.get<FormativeFollowup>(`${this.api}/${id}`);
  }

  create(dto: Partial<FormativeFollowup>): Observable<FormativeFollowup> {
    return this.http.post<FormativeFollowup>(this.api, dto);
  }

  update(id: string, dto: Partial<FormativeFollowup>): Observable<FormativeFollowup> {
    return this.http.put<FormativeFollowup>(`${this.api}/${id}`, dto);
  }

  finalizePreceptor(id: string, signerName: string): Observable<FormativeFollowup> {
    return this.http.post<FormativeFollowup>(`${this.api}/${id}/finalize-preceptor`, { signerName });
  }

  finalizeStudent(id: string, signerName: string): Observable<FormativeFollowup> {
    return this.http.post<FormativeFollowup>(`${this.api}/${id}/finalize-student`, { signerName });
  }
}
