import { Routes } from '@angular/router';
import { AuthPage } from './auth/auth-page';
import { authGuard, guestGuard, homeGuard } from './auth/auth.guards';

export const routes: Routes = [
  // Componentless public home: guests stay here, signed-in users go to /my-documents.
  { path: '', canActivate: [homeGuard], children: [] },
  { path: 'auth', component: AuthPage, canActivate: [guestGuard] },
  // Componentless shell: guarded, and where the documents UI will be added.
  { path: 'my-documents', canActivate: [authGuard], children: [] },
];
