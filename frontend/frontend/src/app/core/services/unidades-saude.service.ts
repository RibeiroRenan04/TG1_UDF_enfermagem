import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  UnidadeSaude, CriarUnidadeSaude, GeocodificacaoResposta,
  ImportPreview, ImportacaoResultado, ImportacaoProgresso,
  Alocacao, EstagiarioDisponivel, StatusGeocodificacao
} from '../models/models';

/**
 * Unidades de saúde e alocação de estagiários.
 *
 * A geocodificação passa sempre por esta API — o frontend nunca chama o
 * Nominatim direto, para que o limite de uso, o User-Agent e o cache fiquem
 * concentrados no backend.
 */
@Injectable({ providedIn: 'root' })
export class UnidadesSaudeService {
  private readonly api = `${environment.apiUrl}/unidades-saude`;
  private readonly apiBase = environment.apiUrl;

  constructor(private http: HttpClient) {}

  // ── Unidades ──────────────────────────────────────────────────────────────
  getAll(filtros?: {
    nome?: string; tipo?: string; cidade?: string;
    ativo?: boolean; statusGeocodificacao?: StatusGeocodificacao;
  }): Observable<UnidadeSaude[]> {
    let params = new HttpParams();
    if (filtros?.nome) params = params.set('nome', filtros.nome);
    if (filtros?.tipo) params = params.set('tipo', filtros.tipo);
    if (filtros?.cidade) params = params.set('cidade', filtros.cidade);
    if (filtros?.ativo !== undefined && filtros.ativo !== null)
      params = params.set('ativo', filtros.ativo);
    if (filtros?.statusGeocodificacao)
      params = params.set('statusGeocodificacao', filtros.statusGeocodificacao);
    return this.http.get<UnidadeSaude[]>(this.api, { params });
  }

  /** Unidades cuja localização precisa de conferência. */
  getPendentesRevisao(): Observable<UnidadeSaude[]> {
    return this.http.get<UnidadeSaude[]>(`${this.api}/pendentes-revisao`);
  }

  get(id: string): Observable<UnidadeSaude> {
    return this.http.get<UnidadeSaude>(`${this.api}/${id}`);
  }

  create(dto: CriarUnidadeSaude): Observable<UnidadeSaude> {
    return this.http.post<UnidadeSaude>(this.api, dto);
  }

  update(id: string, dto: Partial<CriarUnidadeSaude> & { ativo?: boolean }): Observable<UnidadeSaude> {
    return this.http.put<UnidadeSaude>(`${this.api}/${id}`, dto);
  }

  desativar(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/${id}`);
  }

  // ── Geocodificação ────────────────────────────────────────────────────────
  geocodificar(id: string, sobrescreverManual = false): Observable<GeocodificacaoResposta> {
    const params = new HttpParams().set('sobrescreverManual', sobrescreverManual);
    return this.http.post<GeocodificacaoResposta>(`${this.api}/${id}/geocodificar`, {}, { params });
  }

  /** Prévia da localização de um endereço ainda não salvo. */
  preverEndereco(dto: {
    nome?: string; endereco?: string; numero?: string;
    bairro?: string; cidade?: string; uf?: string; cep?: string;
  }): Observable<GeocodificacaoResposta> {
    return this.http.post<GeocodificacaoResposta>(`${this.api}/prever-endereco`, dto);
  }

  definirCoordenadas(id: string, latitude: number, longitude: number, observacao?: string)
    : Observable<UnidadeSaude> {
    return this.http.put<UnidadeSaude>(`${this.api}/${id}/coordenadas`,
      { latitude, longitude, observacao });
  }

  // ── Importação ────────────────────────────────────────────────────────────
  importarPreview(arquivo: File): Observable<ImportPreview> {
    const form = new FormData();
    form.append('arquivo', arquivo, arquivo.name);
    return this.http.post<ImportPreview>(`${this.api}/importar/preview`, form);
  }

  importarConfirmar(previewId: string, acaoDuplicadas: 'ignorar' | 'atualizar' = 'ignorar')
    : Observable<ImportacaoResultado> {
    return this.http.post<ImportacaoResultado>(`${this.api}/importar/confirmar`,
      { previewId, acaoDuplicadas });
  }

  progressoImportacao(loteId: string): Observable<ImportacaoProgresso> {
    return this.http.get<ImportacaoProgresso>(`${this.api}/importar/${loteId}/progresso`);
  }

  urlModeloPlanilha(): string {
    return `${this.api}/importar/modelo`;
  }

  // ── Alocação ──────────────────────────────────────────────────────────────
  getEstagiarios(unidadeId: string, incluirEncerradas = false): Observable<Alocacao[]> {
    const params = new HttpParams().set('incluirEncerradas', incluirEncerradas);
    return this.http.get<Alocacao[]>(`${this.api}/${unidadeId}/estagiarios`, { params });
  }

  getEstagiariosDisponiveis(unidadeId: string, busca?: string): Observable<EstagiarioDisponivel[]> {
    let params = new HttpParams();
    if (busca) params = params.set('busca', busca);
    return this.http.get<EstagiarioDisponivel[]>(
      `${this.api}/${unidadeId}/estagiarios-disponiveis`, { params });
  }

  alocar(unidadeId: string, estagiarioId: string, opcoes?: {
    dataInicio?: string; observacao?: string; encerrarAlocacaoAtual?: boolean;
  }): Observable<Alocacao> {
    return this.http.post<Alocacao>(`${this.api}/${unidadeId}/estagiarios`, {
      estagiarioId,
      dataInicio: opcoes?.dataInicio,
      observacao: opcoes?.observacao,
      encerrarAlocacaoAtual: opcoes?.encerrarAlocacaoAtual ?? false
    });
  }

  encerrarAlocacao(unidadeId: string, estagiarioId: string, observacao?: string): Observable<Alocacao> {
    return this.http.request<Alocacao>('delete',
      `${this.api}/${unidadeId}/estagiarios/${estagiarioId}`, { body: { observacao } });
  }

  getAlocacoes(filtros?: {
    unidadeId?: string; estagiarioId?: string; ativo?: boolean; de?: string; ate?: string;
  }): Observable<Alocacao[]> {
    let params = new HttpParams();
    if (filtros?.unidadeId) params = params.set('unidadeId', filtros.unidadeId);
    if (filtros?.estagiarioId) params = params.set('estagiarioId', filtros.estagiarioId);
    if (filtros?.ativo !== undefined && filtros.ativo !== null)
      params = params.set('ativo', filtros.ativo);
    if (filtros?.de) params = params.set('de', filtros.de);
    if (filtros?.ate) params = params.set('ate', filtros.ate);
    return this.http.get<Alocacao[]>(`${this.apiBase}/alocacoes`, { params });
  }

  /** Unidade do estagiário. O aluno só consegue consultar a própria. */
  getUnidadeDoEstagiario(estagiarioId: string): Observable<Alocacao> {
    return this.http.get<Alocacao>(`${this.apiBase}/estagiarios/${estagiarioId}/unidade`);
  }

  getHistoricoDoEstagiario(estagiarioId: string): Observable<Alocacao[]> {
    return this.http.get<Alocacao[]>(`${this.apiBase}/estagiarios/${estagiarioId}/alocacoes`);
  }
}
