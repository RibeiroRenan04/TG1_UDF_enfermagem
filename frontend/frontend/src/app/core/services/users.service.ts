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

  /**
   * Define as turmas do aluno de uma vez: a lista enviada passa a ser o vínculo
   * completo, e a lista vazia desvincula de todas.
   *
   * O aluno pode ficar em mais de uma turma — dois módulos de estágio no mesmo
   * período, ou uma turma de reposição. A API só recusa a agenda impossível
   * (mesmo turno, mesmos dias da semana e períodos sobrepostos).
   */
  assignGroups(userId: string, groupIds: string[]): Observable<void> {
    return this.http.patch<void>(`${this.api}/${userId}/assign-group`, { groupIds });
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

  /**
   * Opções de vínculo institucional do cadastro de preceptor/professor: as
   * unidades de saúde ativas. A lista é fechada — o backend recusa um valor fora
   * dela — para padronizar a entrada de dados.
   */
  getVinculosInstitucionais(): Observable<string[]> {
    return this.http.get<string[]>(`${this.api}/vinculos-institucionais`);
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

  /** Marca o aluno como concluinte/formado: vai para "Alunos inativos", com histórico preservado. */
  concluir(userId: string): Observable<UserDto> {
    return this.http.post<UserDto>(`${this.api}/${userId}/concluir`, {});
  }

  /** Volta a senha do aluno para o RGM; ele cria uma nova no próximo acesso. */
  resetarSenha(userId: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.api}/${userId}/reset-password`, {});
  }

  /** Desfaz uma conclusão marcada por engano: o aluno volta para "Alunos ativos". */
  reativar(userId: string): Observable<UserDto> {
    return this.http.post<UserDto>(`${this.api}/${userId}/reativar`, {});
  }
}
