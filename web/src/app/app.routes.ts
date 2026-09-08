import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Meridian — Entrar',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login-page').then((m) => m.LoginPage),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./features/shell/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        title: 'Meridian — Dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard-page').then((m) => m.DashboardPage),
      },
      {
        path: 'transfer',
        title: 'Meridian — Nova transferência',
        loadComponent: () =>
          import('./features/transfer/transfer-page').then((m) => m.TransferPage),
      },
      {
        path: 'accounts/:id',
        title: 'Meridian — Extrato',
        loadComponent: () =>
          import('./features/account-detail/account-detail-page').then((m) => m.AccountDetailPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
