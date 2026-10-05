import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import env from '../../environments/environment';
import { routes } from '../app.routes';
import { authInterceptor } from '../auth/auth.interceptor';
import { DocumentsPage } from './documents-page';
import { DocumentSummary } from './documents.service';

describe('DocumentsPage', () => {
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [DocumentsPage],
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter(routes),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
    localStorage.clear();
  });

  const documentsUrl = `${env.API_URL}/api/documents`;

  /** Renders the page and settles the list request it triggers on creation. */
  async function createPage(
    documents: DocumentSummary[] = [],
  ): Promise<{ fixture: ComponentFixture<DocumentsPage>; root: HTMLElement }> {
    const fixture = TestBed.createComponent(DocumentsPage);
    fixture.detectChanges();

    httpTesting.expectOne({ method: 'GET', url: documentsUrl }).flush(documents);
    await fixture.whenStable();
    fixture.detectChanges();

    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  function selectFile(root: HTMLElement, file: File): void {
    const input = root.querySelector('#document-file') as HTMLInputElement;
    // jsdom has no DataTransfer, so shadow the read-only `files` getter directly.
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
  }

  function uploadButton(root: HTMLElement): HTMLButtonElement {
    return root.querySelector('#upload-document') as HTMLButtonElement;
  }

  function buttonByText(root: HTMLElement, text: string): HTMLButtonElement {
    const button = Array.from(root.querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === text,
    );
    if (!button) {
      throw new Error(`No button labelled "${text}" was rendered.`);
    }
    return button;
  }

  function sampleDocument(overrides: Partial<DocumentSummary> = {}): DocumentSummary {
    return {
      id: 'document-1',
      fileName: 'notes.md',
      contentType: 'text/markdown',
      sizeInBytes: 2048,
      uploadedAtUtc: new Date().toISOString(),
      // Ready by default: a settled document never starts the polling timer, which keeps
      // `whenStable()` and `httpTesting.verify()` meaningful in every other spec.
      status: 'Ready',
      errorMessage: null,
      ...overrides,
    };
  }

  it('disables upload until a file is chosen', async () => {
    const { fixture, root } = await createPage();

    expect(uploadButton(root).disabled).toBe(true);

    selectFile(root, new File(['# Notes'], 'notes.md', { type: 'text/markdown' }));
    fixture.detectChanges();

    expect(root.textContent).toContain('notes.md');
    expect(uploadButton(root).disabled).toBe(false);
  });

  it('uploads the chosen file and refreshes the list', async () => {
    const { fixture, root } = await createPage();

    selectFile(root, new File(['# Notes'], 'notes.md', { type: 'text/markdown' }));
    fixture.detectChanges();

    uploadButton(root).click();
    fixture.detectChanges();

    const upload = httpTesting.expectOne({ method: 'POST', url: documentsUrl });
    const body = upload.request.body as FormData;
    expect((body.get('file') as File).name).toBe('notes.md');
    upload.flush(sampleDocument());

    httpTesting.expectOne({ method: 'GET', url: documentsUrl }).flush([sampleDocument()]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.textContent).toContain('notes.md');
    expect(root.textContent).toContain('2.0 KB');
  });

  it('shows the server message when the upload is rejected', async () => {
    const { fixture, root } = await createPage();

    selectFile(root, new File(['x'], 'photo.png', { type: 'image/png' }));
    fixture.detectChanges();

    uploadButton(root).click();
    fixture.detectChanges();

    httpTesting.expectOne({ method: 'POST', url: documentsUrl }).flush(
      {
        title: 'One or more validation errors occurred.',
        errors: { file: ['Choose a PDF, DOCX, MD, or TXT file.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'Choose a PDF, DOCX, MD, or TXT file.',
    );
  });

  it('deletes a document only after the inline confirmation', async () => {
    const { fixture, root } = await createPage([sampleDocument()]);

    buttonByText(root, 'Delete').click();
    fixture.detectChanges();

    // The first click only arms the row; no request may be sent yet.
    httpTesting.expectNone({ method: 'DELETE' });
    expect(root.textContent).toContain('Delete this document?');

    buttonByText(root, 'Cancel').click();
    fixture.detectChanges();

    httpTesting.expectNone({ method: 'DELETE' });
    expect(buttonByText(root, 'Delete').textContent?.trim()).toBe('Delete');

    buttonByText(root, 'Delete').click();
    fixture.detectChanges();
    buttonByText(root, 'Confirm').click();
    fixture.detectChanges();

    httpTesting
      .expectOne({ method: 'DELETE', url: `${documentsUrl}/document-1` })
      .flush(null, { status: 204, statusText: 'No Content' });

    httpTesting.expectOne({ method: 'GET', url: documentsUrl }).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.textContent).toContain('No documents yet');
  });

  it('downloads a document as a blob under its original name', async () => {
    const { fixture, root } = await createPage([sampleDocument()]);

    // jsdom implements neither object-URL method, so stub them and restore afterwards.
    const originalCreateObjectUrl = URL.createObjectURL;
    const originalRevokeObjectUrl = URL.revokeObjectURL;
    const createObjectUrl = vi.fn(() => 'blob:mock-url');
    const revokeObjectUrl = vi.fn();
    URL.createObjectURL = createObjectUrl;
    URL.revokeObjectURL = revokeObjectUrl;

    let clickedAnchor: HTMLAnchorElement | null = null;
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(function (this: HTMLAnchorElement) {
        clickedAnchor = this;
      });

    try {
      buttonByText(root, 'Download').click();
      fixture.detectChanges();

      const download = httpTesting.expectOne({
        method: 'GET',
        url: `${documentsUrl}/document-1/download`,
      });
      expect(download.request.responseType).toBe('blob');
      download.flush(new Blob(['# Notes'], { type: 'text/markdown' }));

      await fixture.whenStable();
      fixture.detectChanges();

      expect(createObjectUrl).toHaveBeenCalledWith(expect.any(Blob));
      expect(clickedAnchor?.download).toBe('notes.md');
    } finally {
      click.mockRestore();
      URL.createObjectURL = originalCreateObjectUrl;
      URL.revokeObjectURL = originalRevokeObjectUrl;
    }
  });

  it('shows a message when the download is not found', async () => {
    const { fixture, root } = await createPage([sampleDocument()]);

    buttonByText(root, 'Download').click();
    fixture.detectChanges();

    httpTesting
      .expectOne({ method: 'GET', url: `${documentsUrl}/document-1/download` })
      .flush(new Blob(['{}'], { type: 'application/json' }), {
        status: 404,
        statusText: 'Not Found',
      });

    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'That document is no longer available.',
    );
  });

  it('shows the ingestion status for each document', async () => {
    const { root } = await createPage([
      sampleDocument({ status: 'Pending' }),
      sampleDocument({ id: 'document-2', fileName: 'paper.pdf', status: 'Processing' }),
      sampleDocument({ id: 'document-3', fileName: 'report.docx', status: 'Ready' }),
    ]);

    expect(root.textContent).toContain('Pending');
    expect(root.textContent).toContain('Processing');
    expect(root.textContent).toContain('Ready');
    // Only a document that is actively moving gets a spinner.
    expect(root.querySelectorAll('svg.animate-spin')).toHaveLength(1);
  });

  it('shows the failure reason for a document that could not be processed', async () => {
    const { root } = await createPage([
      sampleDocument({
        fileName: 'scan.pdf',
        status: 'Failed',
        errorMessage: 'No text could be extracted from this PDF. It may be a scan of images.',
      }),
    ]);

    expect(root.textContent).toContain('Failed');
    expect(root.textContent).toContain('No text could be extracted from this PDF.');
  });

  it('polls while a document is processing and stops once it is ready', async () => {
    // Fake timers must be installed before the component exists, because the polling interval
    // is created from the service constructor.
    vi.useFakeTimers();

    try {
      const fixture = TestBed.createComponent(DocumentsPage);
      fixture.detectChanges();

      httpTesting
        .expectOne({ method: 'GET', url: documentsUrl })
        .flush([sampleDocument({ status: 'Processing' })]);
      // Let the polling effect run and subscribe to the interval.
      await Promise.resolve();
      fixture.detectChanges();

      await vi.advanceTimersByTimeAsync(3000);
      httpTesting
        .expectOne({ method: 'GET', url: documentsUrl })
        .flush([sampleDocument({ status: 'Processing' })]);
      fixture.detectChanges();

      await vi.advanceTimersByTimeAsync(3000);
      httpTesting
        .expectOne({ method: 'GET', url: documentsUrl })
        .flush([sampleDocument({ status: 'Ready' })]);
      fixture.detectChanges();

      // The document settled, so the interval is torn down and nothing else is requested.
      await vi.advanceTimersByTimeAsync(30_000);
      httpTesting.expectNone({ method: 'GET', url: documentsUrl });
    } finally {
      vi.useRealTimers();
    }
  });
});
