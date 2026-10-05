import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { finalize } from 'rxjs';
import { flattenProblemDetails } from '../shared/problem-details';
import { DocumentsService } from './documents.service';

@Component({
  imports: [DatePipe],
  selector: 'app-documents-page',
  templateUrl: './documents-page.html',
})
export class DocumentsPage {
  private readonly documentsService = inject(DocumentsService);
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  protected readonly documents = this.documentsService.documents;
  protected readonly isLoading = this.documentsService.isLoading;
  protected readonly loadError = this.documentsService.loadError;
  protected readonly selectedFile = signal<File | null>(null);
  protected readonly isUploading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** The row whose Delete button has been pressed once and is awaiting confirmation. */
  protected readonly pendingDeleteId = signal<string | null>(null);
  protected readonly deletingId = signal<string | null>(null);
  protected readonly canUpload = computed(
    () => this.selectedFile() !== null && !this.isUploading(),
  );

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.errorMessage.set(null);
    this.selectedFile.set(input.files?.[0] ?? null);
  }

  protected upload(): void {
    const file = this.selectedFile();
    if (!file || this.isUploading()) {
      return;
    }

    this.errorMessage.set(null);
    this.isUploading.set(true);

    this.documentsService
      .upload(file)
      .pipe(finalize(() => this.isUploading.set(false)))
      .subscribe({
        next: () => {
          // Clear the input so choosing the same file again still fires `change`.
          this.resetFileInput();
          this.selectedFile.set(null);
          this.documentsService.reload();
        },
        error: (error: unknown) => {
          this.errorMessage.set(
            this.errorMessageFor(error, 'The upload failed. Please try again.'),
          );
        },
      });
  }

  protected confirmDelete(id: string): void {
    this.errorMessage.set(null);
    this.pendingDeleteId.set(id);
  }

  protected cancelDelete(): void {
    this.pendingDeleteId.set(null);
  }

  protected deleteDocument(id: string): void {
    if (this.deletingId() !== null) {
      return;
    }

    this.errorMessage.set(null);
    this.deletingId.set(id);

    this.documentsService
      .delete(id)
      .pipe(finalize(() => this.deletingId.set(null)))
      .subscribe({
        next: () => {
          this.pendingDeleteId.set(null);
          this.documentsService.reload();
        },
        error: (error: unknown) => {
          this.pendingDeleteId.set(null);
          this.errorMessage.set(
            this.errorMessageFor(error, 'The document could not be deleted. Please try again.'),
          );
        },
      });
  }

  protected formatSize(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }

    const units = ['KB', 'MB', 'GB'];
    let value = bytes / 1024;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit += 1;
    }

    return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unit]}`;
  }

  protected listErrorMessage(error: unknown): string {
    return this.errorMessageFor(error, 'Could not load your documents.');
  }

  private errorMessageFor(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) {
        return 'Could not reach the server. Check that the API is running.';
      }

      return flattenProblemDetails(error.error) ?? fallback;
    }

    return fallback;
  }

  private resetFileInput(): void {
    const input = this.fileInput()?.nativeElement;
    if (input) {
      input.value = '';
    }
  }
}
