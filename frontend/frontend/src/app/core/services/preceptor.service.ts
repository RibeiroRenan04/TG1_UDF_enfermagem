import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class PreceptorService {
  private readonly api = `${environment.apiUrl}/preceptor`;
  constructor(private http: HttpClient) {}

  getStudents(): Observable<any[]> {
    return this.http.get<any[]>(`${this.api}/students`);
  }

  getIrregularRecords(): Observable<any[]> {
    return this.http.get<any[]>(`${this.api}/irregular-records`);
  }
}
