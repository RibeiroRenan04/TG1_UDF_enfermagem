import { StatusGeocodificacao } from '../../core/models/models';

/** Como cada situação da geocodificação aparece nas telas. */
export interface DescricaoStatus {
  rotulo: string;
  icone: string;
  classe: string;
  /** Situação que o administrador precisa resolver. */
  exigeAtencao: boolean;
}

export const STATUS_GEO: Record<string, DescricaoStatus> = {
  pendente:       { rotulo: 'Na fila',        icone: 'schedule',      classe: 'geo-pendente',  exigeAtencao: true },
  processando:    { rotulo: 'Processando',    icone: 'sync',          classe: 'geo-pendente',  exigeAtencao: true },
  sucesso:        { rotulo: 'Localizada',     icone: 'check_circle',  classe: 'geo-sucesso',   exigeAtencao: false },
  revisao_manual: { rotulo: 'Revisar',        icone: 'help',          classe: 'geo-revisao',   exigeAtencao: true },
  nao_encontrado: { rotulo: 'Não encontrada', icone: 'search_off',    classe: 'geo-erro',      exigeAtencao: true },
  erro:           { rotulo: 'Erro',           icone: 'error',         classe: 'geo-erro',      exigeAtencao: true }
};

/** Rótulo da origem das coordenadas. */
export const ORIGEM_COORDENADAS: Record<string, string> = {
  NOMINATIM: 'OpenStreetMap (Nominatim)',
  MANUAL: 'Definida manualmente',
  OUTRO: 'Outra origem'
};
