import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Evaluation } from '../models/models';

@Injectable({ providedIn: 'root' })
export class EvaluationsService {
  private readonly api = `${environment.apiUrl}/evaluations`;
  constructor(private http: HttpClient) {}

  getAll(): Observable<Evaluation[]> {
    return this.http.get<Evaluation[]>(this.api);
  }

  create(dto: {
    studentId: string;
    scheduleId?: string;
    activitiesScore: number;
    postureScore: number;
    planningScore: number;
    comment?: string;
  }): Observable<Evaluation> {
    return this.http.post<Evaluation>(this.api, dto);
  }
}
