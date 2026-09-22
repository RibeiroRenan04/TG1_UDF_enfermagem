import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  UnidadeSaude, CriarUnidadeSaude, GeocodificacaoResposta,
  ImportPreview, ImportacaoResultado, ImportacaoProgresso,
  Alocacao, AlocacoesPagina, EstagiarioDisponivel, StatusGeocodificacao, Turno
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

  /**
   * Modelo da planilha, baixado pelo HttpClient.
   *
   * Um `<a href>` apontando direto para a rota abria a URL sem o cabeçalho
   * Authorization — o token só entra pelo interceptor — e o endpoint, que exige
   * perfil de gestão, respondia 401. Vindo como blob, a requisição leva o token
   * e o arquivo é salvo pela própria tela.
   */
  baixarModeloPlanilha(): Observable<Blob> {
    return this.http.get(`${this.api}/importar/modelo`, { responseType: 'blob' });
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

  /** A alocação é por turno: o mesmo aluno pode ter uma em cada turno. */
  alocar(unidadeId: string, estagiarioId: string, opcoes?: {
    dataInicio?: string; observacao?: string; encerrarAlocacaoAtual?: boolean; turno?: Turno;
  }): Observable<Alocacao> {
    return this.http.post<Alocacao>(`${this.api}/${unidadeId}/estagiarios`, {
      estagiarioId,
      dataInicio: opcoes?.dataInicio,
      observacao: opcoes?.observacao,
      encerrarAlocacaoAtual: opcoes?.encerrarAlocacaoAtual ?? false,
      turno: opcoes?.turno
    });
  }

  /** Encerra a alocação de um turno específico do estagiário nesta unidade. */
  encerrarAlocacao(unidadeId: string, estagiarioId: string, turno?: Turno, observacao?: string)
    : Observable<Alocacao> {
    let params = new HttpParams();
    if (turno) params = params.set('turno', turno);
    return this.http.request<Alocacao>('delete',
      `${this.api}/${unidadeId}/estagiarios/${estagiarioId}`, { body: { observacao }, params });
  }

  /** Alocações filtradas, uma página por vez; `pagina` começa em 1. */
  getAlocacoes(filtros?: {
    unidadeId?: string; estagiarioId?: string; ativo?: boolean; turno?: Turno;
    de?: string; ate?: string; busca?: string; pagina?: number; tamanhoPagina?: number;
  }): Observable<AlocacoesPagina> {
    let params = new HttpParams();
    if (filtros?.unidadeId) params = params.set('unidadeId', filtros.unidadeId);
    if (filtros?.estagiarioId) params = params.set('estagiarioId', filtros.estagiarioId);
    if (filtros?.ativo !== undefined && filtros.ativo !== null)
      params = params.set('ativo', filtros.ativo);
    if (filtros?.turno) params = params.set('turno', filtros.turno);
    if (filtros?.de) params = params.set('de', filtros.de);
    if (filtros?.ate) params = params.set('ate', filtros.ate);
    if (filtros?.busca) params = params.set('busca', filtros.busca);
    if (filtros?.pagina) params = params.set('pagina', filtros.pagina);
    if (filtros?.tamanhoPagina) params = params.set('tamanhoPagina', filtros.tamanhoPagina);
    return this.http.get<AlocacoesPagina>(`${this.apiBase}/alocacoes`, { params });
  }

  /** Unidade do estagiário no turno pedido. O aluno só consulta a própria. */
  getUnidadeDoEstagiario(estagiarioId: string, turno?: Turno): Observable<Alocacao> {
    let params = new HttpParams();
    if (turno) params = params.set('turno', turno);
    return this.http.get<Alocacao>(`${this.apiBase}/estagiarios/${estagiarioId}/unidade`, { params });
  }

  /** Todas as alocações ativas do estagiário, uma por turno. */
  getUnidadesDoEstagiario(estagiarioId: string): Observable<Alocacao[]> {
    return this.http.get<Alocacao[]>(`${this.apiBase}/estagiarios/${estagiarioId}/unidades`);
  }

  getHistoricoDoEstagiario(estagiarioId: string): Observable<Alocacao[]> {
    return this.http.get<Alocacao[]>(`${this.apiBase}/estagiarios/${estagiarioId}/alocacoes`);
  }
}
