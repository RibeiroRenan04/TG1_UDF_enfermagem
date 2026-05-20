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
    return this.http.get<UserDto[]>(`${this.api}?role=aluno`);
  }

  assignGroup(userId: string, groupId: string | null): Observable<void> {
    return this.http.patch<void>(`${this.api}/${userId}/assign-group`, { groupId });
  }

  /** Cadastra preceptor/supervisor manualmente */
  createStaff(dto: {
    fullName: string;
    email: string;
    password: string;
    role: 'preceptor' | 'supervisor';
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
