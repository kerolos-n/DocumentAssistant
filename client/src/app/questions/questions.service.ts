import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import env from '../../environments/environment';

/**
 * Why an answer is (or isn't) available. Mirrors the server's `AnswerOutcome`, which is
 * serialized as a string. `NoDocuments` and `NoRelevantContext` both come back with
 * `isAnswerable: false`, but the page shows them differently.
 */
export type AnswerOutcome = 'Answered' | 'NoDocuments' | 'NoRelevantContext';

/** One chunk the answer was built from. */
export interface Citation {
  readonly documentId: string;
  readonly fileName: string;
  readonly pageNumber: number | null;
  readonly snippet: string;
}

export interface Answer {
  readonly isAnswerable: boolean;
  readonly outcome: AnswerOutcome;
  readonly answer: string;
  readonly citations: readonly Citation[];
}

@Injectable({ providedIn: 'root' })
export class QuestionsService {
  private readonly http = inject(HttpClient);
  private readonly questionsUrl = `${env.API_URL}/api/questions`;

  /** Asks a question against the signed-in user's documents. */
  ask(question: string): Observable<Answer> {
    return this.http.post<Answer>(this.questionsUrl, { question });
  }
}
