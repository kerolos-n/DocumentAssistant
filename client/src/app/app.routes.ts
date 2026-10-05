import { Routes } from '@angular/router';
import { AuthPage } from './auth/auth-page';
import { authGuard, guestGuard, homeGuard } from './auth/auth.guards';
import { DocumentsPage } from './documents/documents-page';
import { AskPage } from './questions/ask-page';

export const routes: Routes = [
  // Componentless public home: guests stay here, signed-in users go to /my-documents.
  { path: '', canActivate: [homeGuard], children: [] },
  { path: 'auth', component: AuthPage, canActivate: [guestGuard] },
  // Componentless shell: guarded, with the documents UI rendered by its default child.
  {
    path: 'my-documents',
    canActivate: [authGuard],
    children: [{ path: '', component: DocumentsPage }],
  },
  { path: 'ask', component: AskPage, canActivate: [authGuard] },
];
