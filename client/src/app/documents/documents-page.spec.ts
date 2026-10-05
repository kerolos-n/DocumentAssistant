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
    return root.querySelector('button[type="button"]') as HTMLButtonElement;
  }

  function sampleDocument(overrides: Partial<DocumentSummary> = {}): DocumentSummary {
    return {
      id: 'document-1',
      fileName: 'notes.md',
      contentType: 'text/markdown',
      sizeInBytes: 2048,
      uploadedAtUtc: new Date().toISOString(),
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
});
