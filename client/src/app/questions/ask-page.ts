import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { DocumentsService, DocumentStatus } from '../documents/documents.service';
import { httpErrorMessage } from '../shared/http-error';
import { Answer, QuestionsService } from './questions.service';

/** Mirrors the server's `AskQuestion.MaxQuestionLength`; the two must stay in step. */
const MAX_QUESTION_LENGTH = 2000;

@Component({
  imports: [RouterLink],
  selector: 'app-ask-page',
  templateUrl: './ask-page.html',
})
export class AskPage {
  private readonly questionsService = inject(QuestionsService);
  private readonly documentsService = inject(DocumentsService);

  /** The documents sidebar reads the shared, polled list owned by DocumentsService. */
  protected readonly documents = this.documentsService.documents;
  protected readonly isLoadingDocuments = this.documentsService.isLoading;
  protected readonly documentsError = this.documentsService.loadError;

  protected readonly question = signal('');
  protected readonly isLoading = signal(false);
  protected readonly answer = signal<Answer | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly maxQuestionLength = MAX_QUESTION_LENGTH;

  /**
   * Status dot colours. Complete class names, not fragments, so Tailwind's scanner emits them.
   */
  protected readonly statusDotClasses: Readonly<Record<DocumentStatus, string>> = {
    Pending: 'bg-amber-400',
    Processing: 'bg-sky-400',
    Ready: 'bg-pine-600',
    Failed: 'bg-red-500',
  };

  /** True once the text exceeds what the server will accept, so the form can say so up front. */
  protected readonly isQuestionTooLong = computed(
    () => this.question().length > MAX_QUESTION_LENGTH,
  );

  /**
   * A blank question is not worth a round trip, an over-long one would only earn a 400, and a
   * second submit while loading is a duplicate.
   */
  protected readonly canSubmit = computed(() => {
    const length = this.question().trim().length;
    return length > 0 && length <= MAX_QUESTION_LENGTH && !this.isLoading();
  });

  protected onQuestionInput(event: Event): void {
    this.question.set((event.target as HTMLTextAreaElement).value);
  }

  protected ask(): void {
    const question = this.question().trim();
    if (question.length === 0 || question.length > MAX_QUESTION_LENGTH || this.isLoading()) {
      return;
    }

    this.errorMessage.set(null);
    this.answer.set(null);
    this.isLoading.set(true);

    this.questionsService
      .ask(question)
      .pipe(finalize(() => this.isLoading.set(false)))
      .subscribe({
        next: (answer) => this.answer.set(answer),
        error: (error: unknown) => this.errorMessage.set(this.errorMessageFor(error)),
      });
  }

  private errorMessageFor(error: unknown): string {
    // Shared status handling covers 429 ("try again in a moment") and 502 (assistant down).
    return httpErrorMessage(error, 'The question could not be answered. Please try again.');
  }
}
