import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'shell',
    loadComponent: () => import('./shell/shell.component').then((m) => m.ShellComponent),
  },
  { path: '', pathMatch: 'full', redirectTo: 'shell' },
];
