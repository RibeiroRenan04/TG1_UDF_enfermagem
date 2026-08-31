import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  Irregularity, IrregularitySummary, CreateIrregularity, IrregularityStatus
} from '../models/models';

/**
 * Irregularidades de ponto. O preceptor apenas registra ciência e observação;
 * aprovar ou negar é ação exclusiva do professor.
 */
@Injectable({ providedIn: 'root' })
export class IrregularitiesService {
  private readonly api = `${environment.apiUrl}/irregularities`;

  constructor(private http: HttpClient) {}

  getAll(status?: IrregularityStatus, studentId?: string): Observable<Irregularity[]> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    if (studentId) params = params.set('studentId', studentId);
    return this.http.get<Irregularity[]>(this.api, { params });
  }

  getById(id: string): Observable<Irregularity> {
    return this.http.get<Irregularity>(`${this.api}/${id}`);
  }

  getSummary(): Observable<IrregularitySummary> {
    return this.http.get<IrregularitySummary>(`${this.api}/summary`);
  }

  /** Registro feito pelo aluno. */
  create(dto: CreateIrregularity): Observable<Irregularity> {
    return this.http.post<Irregularity>(this.api, dto);
  }

  /** Ciência do preceptor + observação; encaminha a ocorrência ao professor. */
  preceptorReview(id: string, note?: string): Observable<Irregularity> {
    return this.http.patch<Irregularity>(`${this.api}/${id}/preceptor-review`, { note });
  }

  /** Decisão final do professor. */
  professorDecision(id: string, approve: boolean, note?: string): Observable<Irregularity> {
    return this.http.patch<Irregularity>(`${this.api}/${id}/professor-decision`, { approve, note });
  }
}
