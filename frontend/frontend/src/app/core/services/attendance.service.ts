import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AttendanceRecord, ActiveSchedule } from '../models/models';

@Injectable({ providedIn: 'root' })
export class AttendanceService {
  private readonly api = `${environment.apiUrl}/attendance`;

  constructor(private http: HttpClient) {}

  getAll(studentId?: string, limit = 200): Observable<AttendanceRecord[]> {
    let params = new HttpParams().set('limit', limit);
    if (studentId) params = params.set('studentId', studentId);
    return this.http.get<AttendanceRecord[]>(this.api, { params });
  }

  getActiveSchedule(): Observable<ActiveSchedule | null> {
    return this.http.get<ActiveSchedule | null>(`${this.api}/active-schedule`);
  }

  getOpenCheckIn(): Observable<{ id: string; recorded_at: string } | null> {
    return this.http.get<{ id: string; recorded_at: string } | null>(`${this.api}/open-check-in`);
  }

  create(dto: {
    latitude: number;
    longitude: number;
    type: string;
    scheduleId?: string;
    locationId?: string;
    activitiesDescription?: string;
    photoBase64?: string;
  }): Observable<AttendanceRecord> {
    return this.http.post<AttendanceRecord>(this.api, dto);
  }

  validate(id: string, approve: boolean, reason?: string): Observable<AttendanceRecord> {
    return this.http.patch<AttendanceRecord>(`${this.api}/${id}/validate`, { approve, reason });
  }
}
