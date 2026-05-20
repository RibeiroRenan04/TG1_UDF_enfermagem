import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Location } from '../models/models';

@Injectable({ providedIn: 'root' })
export class LocationsService {
  private readonly api = `${environment.apiUrl}/locations`;
  constructor(private http: HttpClient) {}

  getAll(): Observable<Location[]> {
    return this.http.get<Location[]>(this.api);
  }

  create(dto: Omit<Location, 'id'>): Observable<Location> {
    return this.http.post<Location>(this.api, dto);
  }

  batchCreate(dtos: Omit<Location, 'id'>[]): Observable<{ inserted: number }> {
    return this.http.post<{ inserted: number }>(`${this.api}/batch`, dtos);
  }

  update(id: string, dto: Omit<Location, 'id'>): Observable<Location> {
    return this.http.put<Location>(`${this.api}/${id}`, dto);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/${id}`);
  }
}
