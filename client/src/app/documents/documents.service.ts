import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { finalize } from 'rxjs';
import env from '../../environments/environment';

export interface DocumentSummary {
  readonly id: string;
  readonly fileName: string;
  readonly contentType: string;
  readonly sizeInBytes: number;
  readonly uploadedAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class DocumentsService {
  private readonly http = inject(HttpClient);
  private readonly documentsUrl = `${env.API_URL}/api/documents`;

  private readonly documentsState = signal<DocumentSummary[]>([]);
  private readonly loadingState = signal(false);
  private readonly errorState = signal<unknown>(null);

  /** The signed-in user's documents, newest first. */
  readonly documents = this.documentsState.asReadonly();
  readonly isLoading = this.loadingState.asReadonly();
  readonly loadError = this.errorState.asReadonly();

  constructor() {
    this.reload();
  }

  /**
   * Fetches the user's documents. Imperative signals rather than `httpResource`, because a
   * resource registers a pending task that keeps `fixture.whenStable()` from resolving in the
   * existing specs that navigate to `/my-documents`; the `authInterceptor` still attaches the
   * bearer token because the URL starts with `api/`.
   */
  reload(): void {
    this.loadingState.set(true);
    this.errorState.set(null);

    this.http
      .get<DocumentSummary[]>(this.documentsUrl)
      .pipe(finalize(() => this.loadingState.set(false)))
      .subscribe({
        next: (documents) => this.documentsState.set(documents),
        error: (error: unknown) => this.errorState.set(error),
      });
  }

  upload(file: File) {
    const formData = new FormData();
    // The API's minimal-API endpoint binds this by the parameter name `file`.
    formData.append('file', file);

    // app.config.ts uses withFetch(), and the fetch backend emits no upload-progress events,
    // so callers show an indeterminate busy state instead of a percentage.
    return this.http.post<DocumentSummary>(this.documentsUrl, formData);
  }

  /** Removes the document's metadata row and its blob. The API scopes this to the owner. */
  delete(id: string) {
    return this.http.delete<void>(`${this.documentsUrl}/${encodeURIComponent(id)}`);
  }
}
