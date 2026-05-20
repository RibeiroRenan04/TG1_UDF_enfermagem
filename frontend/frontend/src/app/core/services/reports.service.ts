import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ReportRow } from '../models/models';

@Injectable({ providedIn: 'root' })
export class ReportsService {
  private readonly api = `${environment.apiUrl}/reports`;
  constructor(private http: HttpClient) {}
  get(): Observable<ReportRow[]> { return this.http.get<ReportRow[]>(this.api); }
}
