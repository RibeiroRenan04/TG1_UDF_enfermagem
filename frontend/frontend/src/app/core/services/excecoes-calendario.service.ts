import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AbrangenciaExcecao, ExcecaoCalendario, TipoExcecao, Turno } from '../models/models';

/** Dados de criação e edição de uma exceção do calendário. */
export interface ExcecaoCalendarioForm {
  type: TipoExcecao;
  scope: AbrangenciaExcecao;
  startDate: string;
  endDate?: string;
  shift?: Turno;
  groupId?: string;
  scheduleId?: string;
  studentId?: string;
  course?: string;
  locationId?: string;
  remoteActivityId?: string;
  description: string;
}

/**
 * Calendário de exceções: feriados, recessos, estágios cancelados, trocas de
 * local, dias que viraram remotos, atividades especiais e reposições.
 *
 * A exceção é cadastrada uma vez com a sua abrangência e a programação do dia a
 * aplica sozinha — não é preciso alterar aluno por aluno.
 */
@Injectable({ providedIn: 'root' })
export class ExcecoesCalendarioService {
  private readonly api = `${environment.apiUrl}/excecoes-calendario`;

  constructor(private http: HttpClient) {}

  getAll(filtros?: {
    de?: string; ate?: string; tipo?: TipoExcecao;
    abrangencia?: AbrangenciaExcecao; groupId?: string;
  }): Observable<ExcecaoCalendario[]> {
    let params = new HttpParams();
    if (filtros?.de) params = params.set('de', filtros.de);
    if (filtros?.ate) params = params.set('ate', filtros.ate);
    if (filtros?.tipo) params = params.set('tipo', filtros.tipo);
    if (filtros?.abrangencia) params = params.set('abrangencia', filtros.abrangencia);
    if (filtros?.groupId) params = params.set('groupId', filtros.groupId);
    return this.http.get<ExcecaoCalendario[]>(this.api, { params });
  }

  get(id: string): Observable<ExcecaoCalendario> {
    return this.http.get<ExcecaoCalendario>(`${this.api}/${id}`);
  }

  create(dto: ExcecaoCalendarioForm): Observable<ExcecaoCalendario> {
    return this.http.post<ExcecaoCalendario>(this.api, dto);
  }

  update(id: string, dto: ExcecaoCalendarioForm): Observable<ExcecaoCalendario> {
    return this.http.put<ExcecaoCalendario>(`${this.api}/${id}`, dto);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/${id}`);
  }
}
