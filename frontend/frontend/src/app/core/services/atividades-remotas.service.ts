import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AtividadeRemota, AtividadeRemotaAluno, ParticipacaoAtividade,
  PresencaRemotaResultado, TipoTarefaRemota
} from '../models/models';

/** Dados de criação e edição de uma atividade remota. */
export interface AtividadeRemotaForm {
  title: string;
  description?: string;
  groupId: string;
  scheduleId?: string;
  activityDate: string;
  startTime: string;
  endTime: string;
  estimatedHours: number;
  requiresTask: boolean;
  taskType?: TipoTarefaRemota;
  taskInstructions?: string;
}

/**
 * Atividades remotas e o registro de presença por código.
 *
 * O código de presença só aparece nas respostas do professor: para o aluno, ele
 * é justamente o que precisa vir de fora do sistema para comprovar o acesso.
 */
@Injectable({ providedIn: 'root' })
export class AtividadesRemotasService {
  private readonly api = `${environment.apiUrl}/atividades-remotas`;

  constructor(private http: HttpClient) {}

  // ── Professor ─────────────────────────────────────────────────────────────
  getAll(filtros?: { groupId?: string; de?: string; ate?: string; ativo?: boolean })
    : Observable<AtividadeRemota[]> {
    let params = new HttpParams();
    if (filtros?.groupId) params = params.set('groupId', filtros.groupId);
    if (filtros?.de) params = params.set('de', filtros.de);
    if (filtros?.ate) params = params.set('ate', filtros.ate);
    if (filtros?.ativo !== undefined) params = params.set('ativo', filtros.ativo);
    return this.http.get<AtividadeRemota[]>(this.api, { params });
  }

  get(id: string): Observable<AtividadeRemota> {
    return this.http.get<AtividadeRemota>(`${this.api}/${id}`);
  }

  create(dto: AtividadeRemotaForm): Observable<AtividadeRemota> {
    return this.http.post<AtividadeRemota>(this.api, dto);
  }

  update(id: string, dto: AtividadeRemotaForm): Observable<AtividadeRemota> {
    return this.http.put<AtividadeRemota>(`${this.api}/${id}`, dto);
  }

  /** Invalida o código antes do fim da janela, preservando as participações. */
  encerrar(id: string): Observable<AtividadeRemota> {
    return this.http.patch<AtividadeRemota>(`${this.api}/${id}/encerrar`, {});
  }

  /** Gera outro código — usado quando o atual vazou para fora do grupo. */
  novoCodigo(id: string): Observable<AtividadeRemota> {
    return this.http.patch<AtividadeRemota>(`${this.api}/${id}/novo-codigo`, {});
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/${id}`);
  }

  getParticipacoes(id: string): Observable<ParticipacaoAtividade[]> {
    return this.http.get<ParticipacaoAtividade[]>(`${this.api}/${id}/participacoes`);
  }

  // ── Aluno ─────────────────────────────────────────────────────────────────
  minhas(de?: string, ate?: string): Observable<AtividadeRemotaAluno[]> {
    let params = new HttpParams();
    if (de) params = params.set('de', de);
    if (ate) params = params.set('ate', ate);
    return this.http.get<AtividadeRemotaAluno[]>(`${this.api}/minhas`, { params });
  }

  registrarPresenca(code: string, taskResponse?: string): Observable<PresencaRemotaResultado> {
    return this.http.post<PresencaRemotaResultado>(
      `${this.api}/registrar-presenca`, { code, taskResponse });
  }
}
