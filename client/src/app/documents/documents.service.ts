import { HttpClient } from '@angular/common/http';
import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { catchError, EMPTY, finalize, interval, Observable, switchMap } from 'rxjs';
import env from '../../environments/environment';

/**
 * Mirrors the server's `DocumentStatus` enum, which is serialized as a string.
 * `Pending`/`Processing` are still in flight; `Ready`/`Failed` are settled.
 */
export type DocumentStatus = 'Pending' | 'Processing' | 'Ready' | 'Failed';

export interface DocumentSummary {
  readonly id: string;
  readonly fileName: string;
  readonly contentType: string;
  readonly sizeInBytes: number;
  readonly uploadedAtUtc: string;
  readonly status: DocumentStatus;
  readonly errorMessage: string | null;
}

/** How often the list is refreshed while any document is still being ingested. */
const POLL_INTERVAL_MS = 3000;

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

  /** True while a document is still queued or being processed — i.e. while polling pays off. */
  private readonly hasUnsettledDocuments = computed(() =>
    this.documentsState().some(
      (document) => document.status === 'Pending' || document.status === 'Processing',
    ),
  );

  constructor() {
    this.reload();

    // Poll only while something is still being ingested. Reading the computed (not the array)
    // means the effect re-runs when that boolean flips and not on every response, so the
    // interval keeps a steady cadence. Its cleanup unsubscribes when everything has settled,
    // which is also why specs that render only Ready documents never start a timer.
    effect((onCleanup) => {
      if (!this.hasUnsettledDocuments()) {
        return;
      }

      const subscription = interval(POLL_INTERVAL_MS)
        .pipe(
          // Refresh quietly: a poll must not flip `isLoading`, which would replace the list with
          // a loading message every few seconds. A failed poll is swallowed so a blip does not
          // kill the cadence; the next tick tries again.
          switchMap(() =>
            this.http.get<DocumentSummary[]>(this.documentsUrl).pipe(catchError(() => EMPTY)),
          ),
        )
        .subscribe((documents) => this.documentsState.set(documents));

      onCleanup(() => subscription.unsubscribe());
    });
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

  /** Removes the document's metadata row, its blob, and its chunks. The API scopes this to the owner. */
  delete(id: string) {
    return this.http.delete<void>(`${this.documentsUrl}/${encodeURIComponent(id)}`);
  }

  /**
   * Downloads the document's bytes. `responseType: 'blob'` keeps the body binary rather than
   * letting Angular try to parse it as JSON.
   */
  download(id: string): Observable<Blob> {
    return this.http.get(`${this.documentsUrl}/${encodeURIComponent(id)}/download`, {
      responseType: 'blob',
    });
  }
}
