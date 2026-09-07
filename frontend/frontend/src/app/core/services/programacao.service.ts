import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ProgramacaoDia, Turno } from '../models/models';

/**
 * Programação diária do aluno: onde ele deveria estar e o que deveria fazer.
 *
 * A tela de ponto consulta isto antes de tudo — é a resposta que diz se o dia
 * pede localização, se pede o código de uma atividade remota ou se não há ponto
 * a registrar.
 */
@Injectable({ providedIn: 'root' })
export class ProgramacaoService {
  private readonly api = `${environment.apiUrl}/programacao`;

  constructor(private http: HttpClient) {}

  /** Programação de um dia. Sem data, hoje. */
  getDia(opcoes?: { data?: string; turno?: Turno; studentId?: string }): Observable<ProgramacaoDia> {
    let params = new HttpParams();
    if (opcoes?.data) params = params.set('data', opcoes.data);
    if (opcoes?.turno) params = params.set('turno', opcoes.turno);
    if (opcoes?.studentId) params = params.set('studentId', opcoes.studentId);
    return this.http.get<ProgramacaoDia>(this.api, { params });
  }

  /** Calendário do aluno, já com feriados, dias remotos e trocas de local aplicados. */
  getPeriodo(de: string, ate: string, opcoes?: { turno?: Turno; studentId?: string })
    : Observable<ProgramacaoDia[]> {
    let params = new HttpParams().set('de', de).set('ate', ate);
    if (opcoes?.turno) params = params.set('turno', opcoes.turno);
    if (opcoes?.studentId) params = params.set('studentId', opcoes.studentId);
    return this.http.get<ProgramacaoDia[]>(`${this.api}/periodo`, { params });
  }
}
