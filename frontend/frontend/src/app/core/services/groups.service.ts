import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { StudentGroup, RotationSchedule } from '../models/models';

@Injectable({ providedIn: 'root' })
export class GroupsService {
  private readonly api = `${environment.apiUrl}/groups`;
  constructor(private http: HttpClient) {}

  getAll(): Observable<StudentGroup[]> {
    return this.http.get<StudentGroup[]>(this.api);
  }

  create(dto: { code: string; name: string; description?: string }): Observable<StudentGroup> {
    return this.http.post<StudentGroup>(this.api, dto);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/${id}`);
  }

  getSchedules(groupId?: string): Observable<RotationSchedule[]> {
    const url = groupId ? `${this.api}/${groupId}/schedules` : `${this.api}/schedules`;
    return this.http.get<RotationSchedule[]>(url);
  }

  createSchedule(dto: Partial<RotationSchedule>): Observable<RotationSchedule> {
    return this.http.post<RotationSchedule>(`${this.api}/schedules`, dto);
  }

  updateSchedule(id: string, dto: Partial<RotationSchedule>): Observable<RotationSchedule> {
    return this.http.put<RotationSchedule>(`${this.api}/schedules/${id}`, dto);
  }

  deleteSchedule(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/schedules/${id}`);
  }
}
