import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { routes } from './app.routes';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { PaginatorIntlPtBr } from './core/utils/paginator-intl';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideAnimationsAsync(),
    // Paginadores do Material em português, em qualquer tela do sistema.
    { provide: MatPaginatorIntl, useClass: PaginatorIntlPtBr }
  ]
};
