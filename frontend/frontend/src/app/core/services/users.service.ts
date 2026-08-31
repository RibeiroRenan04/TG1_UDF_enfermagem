import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { UserDto, BulkImportStudent, BulkImportResult, AdvanceSemesterResult } from '../models/models';

@Injectable({ providedIn: 'root' })
export class UsersService {
  private readonly api = `${environment.apiUrl}/users`;
  constructor(private http: HttpClient) {}

  getAll(): Observable<UserDto[]> {
    return this.http.get<UserDto[]>(this.api);
  }

  getStudents(): Observable<UserDto[]> {
    return this.http.get<UserDto[]>(`${this.api}/students`);
  }

  /** Preceptores e supervisores — usado na alocação de rodízios. */
  getPreceptors(): Observable<UserDto[]> {
    return this.http.get<UserDto[]>(`${this.api}/preceptors`);
  }

  assignGroup(userId: string, groupId: string | null): Observable<void> {
    return this.http.patch<void>(`${this.api}/${userId}/assign-group`, { groupId });
  }

  /**
   * Autoriza (ou revoga) a chegada do aluno após o horário previsto de início.
   * A carga horária do dia continua sendo exigida.
   */
  setLatePermission(userId: string, allowLateArrival: boolean, note?: string): Observable<UserDto> {
    return this.http.patch<UserDto>(`${this.api}/${userId}/late-permission`, { allowLateArrival, note });
  }

  /** Altera o turno do aluno — usado nas trocas autorizadas entre alunos. */
  updateShift(userId: string, shift: 'manha' | 'tarde' | 'noite'): Observable<UserDto> {
    return this.http.patch<UserDto>(`${this.api}/${userId}/shift`, { shift });
  }

  /** Cadastra preceptor, professor (supervisor) ou coordenadora manualmente */
  createStaff(dto: {
    fullName: string;
    email: string;
    password: string;
    role: 'preceptor' | 'supervisor' | 'coordenadora';
    institution?: string;
    phone?: string;
  }): Observable<UserDto> {
    return this.http.post<UserDto>(`${this.api}/staff`, dto);
  }

  /** Importa alunos em massa a partir de uma lista */
  bulkImportStudents(students: BulkImportStudent[]): Observable<BulkImportResult> {
    return this.http.post<BulkImportResult>(`${this.api}/bulk-import`, { students });
  }

  /** Avança todos os alunos de 7° para 8° semestre e forma os do 8° */
  advanceSemester(): Observable<AdvanceSemesterResult> {
    return this.http.post<AdvanceSemesterResult>(`${this.api}/advance-semester`, {});
  }
}
