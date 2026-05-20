import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DashboardStats, Pendency } from '../models/models';

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
}
