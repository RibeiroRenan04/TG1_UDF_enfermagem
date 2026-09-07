import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DashboardStats, Pendency, PendingStatus } from '../models/models';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly api = `${environment.apiUrl}/dashboard`;
  constructor(private http: HttpClient) {}

  getStats(): Observable<DashboardStats> {
    return this.http.get<DashboardStats>(`${this.api}/stats`);
  }

  getPendencies(studentId?: string): Observable<Pendency[]> {
    const params = studentId ? `?studentId=${studentId}` : '';
    return this.http.get<Pendency[]>(`${this.api}/pendencies${params}`);
  }

  /**
   * Avisos do card "Status Pendentes". Endpoint próprio para a tela recarregar
   * só o card depois de uma ação (registrar irregularidade, por exemplo).
   */
  getPendingStatuses(): Observable<PendingStatus[]> {
    return this.http.get<PendingStatus[]>(`${this.api}/status-pendentes`);
  }
}
