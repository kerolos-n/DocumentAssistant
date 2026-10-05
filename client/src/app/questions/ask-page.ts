import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { flattenProblemDetails } from '../shared/problem-details';
import { Answer, QuestionsService } from './questions.service';

@Component({
  imports: [RouterLink],
  selector: 'app-ask-page',
  templateUrl: './ask-page.html',
})
export class AskPage {
  private readonly questionsService = inject(QuestionsService);

  protected readonly question = signal('');
  protected readonly isLoading = signal(false);
  protected readonly answer = signal<Answer | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  /** A blank question is not worth a round trip, and a second submit while loading is a duplicate. */
  protected readonly canSubmit = computed(
    () => this.question().trim().length > 0 && !this.isLoading(),
  );

  protected onQuestionInput(event: Event): void {
    this.question.set((event.target as HTMLTextAreaElement).value);
  }

  protected ask(): void {
    const question = this.question().trim();
    if (question.length === 0 || this.isLoading()) {
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
    const fallback = 'The question could not be answered. Please try again.';

    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) {
        return 'Could not reach the server. Check that the API is running.';
      }

      // The API answers 502 when the model provider fails or times out.
      if (error.status === 502 || error.status === 504) {
        return 'The assistant is temporarily unavailable. Please try again.';
      }

      return flattenProblemDetails(error.error) ?? fallback;
    }

    return fallback;
  }
}
