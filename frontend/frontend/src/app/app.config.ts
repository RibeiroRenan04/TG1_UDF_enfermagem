import { ApplicationConfig, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { routes } from './app.routes';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { PaginatorIntlPtBr } from './core/utils/paginator-intl';
import { provideDataBr } from './core/utils/data-br';

registerLocaleData(localePt, 'pt-BR');

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideAnimationsAsync(),
    // Paginadores do Material em português, em qualquer tela do sistema.
    { provide: MatPaginatorIntl, useClass: PaginatorIntlPtBr },
    // Datas, números e calendários no padrão brasileiro (dd/MM/aaaa, vírgula decimal).
    { provide: LOCALE_ID, useValue: 'pt-BR' },
    ...provideDataBr()
  ]
};
