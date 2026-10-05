import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import env from '../../environments/environment';
import { routes } from '../app.routes';
import { authInterceptor } from '../auth/auth.interceptor';
import { DocumentSummary } from '../documents/documents.service';
import { AskPage } from './ask-page';
import { Answer } from './questions.service';

describe('AskPage', () => {
  let httpTesting: HttpTestingController;

  const questionsUrl = `${env.API_URL}/api/questions`;
  const documentsUrl = `${env.API_URL}/api/documents`;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [AskPage],
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

  function createPage(documents: DocumentSummary[] = []): {
    fixture: ComponentFixture<AskPage>;
    root: HTMLElement;
  } {
    const fixture = TestBed.createComponent(AskPage);
    fixture.detectChanges();
    // The sidebar reads the shared DocumentsService list, which loads on construction.
    httpTesting.expectOne({ method: 'GET', url: documentsUrl }).flush(documents);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  function sampleDocument(overrides: Partial<DocumentSummary> = {}): DocumentSummary {
    return {
      id: 'document-1',
      fileName: 'handbook.pdf',
      contentType: 'application/pdf',
      sizeInBytes: 1024,
      uploadedAtUtc: new Date().toISOString(),
      status: 'Ready',
      errorMessage: null,
      ...overrides,
    };
  }

  function questionInput(root: HTMLElement): HTMLTextAreaElement {
    return root.querySelector('#question') as HTMLTextAreaElement;
  }

  function askButton(root: HTMLElement): HTMLButtonElement {
    return root.querySelector('#ask-question') as HTMLButtonElement;
  }

  function typeQuestion(
    fixture: ComponentFixture<AskPage>,
    root: HTMLElement,
    value: string,
  ): void {
    const input = questionInput(root);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function submit(fixture: ComponentFixture<AskPage>, root: HTMLElement): void {
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  function sampleAnswer(overrides: Partial<Answer> = {}): Answer {
    return {
      isAnswerable: true,
      outcome: 'Answered',
      answer: 'The handbook says it is available.',
      citations: [
        {
          documentId: 'document-1',
          fileName: 'handbook.pdf',
          pageNumber: 3,
          snippet: 'The handbook says it is available from launch.',
        },
      ],
      ...overrides,
    };
  }

  it('disables Ask until a question is typed', () => {
    const { fixture, root } = createPage();

    expect(askButton(root).disabled).toBe(true);

    typeQuestion(fixture, root, 'When is it available?');

    expect(askButton(root).disabled).toBe(false);
    expect(askButton(root).textContent?.trim()).toBe('Ask');
  });

  it('posts the question and renders the answer with citations', async () => {
    const { fixture, root } = createPage();

    typeQuestion(fixture, root, 'When is it available?');
    submit(fixture, root);

    const request = httpTesting.expectOne({ method: 'POST', url: questionsUrl });
    expect(request.request.body).toEqual({ question: 'When is it available?' });

    // While the request is in flight the button reflects the busy state.
    expect(askButton(root).textContent?.trim()).toBe('Thinking…');

    request.flush(sampleAnswer());
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.textContent).toContain('The handbook says it is available.');
    expect(root.textContent).toContain('Citations');
    expect(root.textContent).toContain('handbook.pdf');
    expect(root.textContent).toContain('page 3');
    expect(root.textContent).toContain('The handbook says it is available from launch.');
  });

  it('lists the documents in the sidebar with a link to manage them', () => {
    const { root } = createPage([sampleDocument({ fileName: 'handbook.pdf', status: 'Failed' })]);

    expect(root.textContent).toContain('handbook.pdf');
    expect(root.textContent).toContain('Failed');
    expect(root.querySelector('a[href="/my-documents"]')?.textContent).toContain(
      'Manage documents',
    );
  });

  it('shows the no-documents message with a link to upload', async () => {
    const { fixture, root } = createPage();

    typeQuestion(fixture, root, 'Anything?');
    submit(fixture, root);

    httpTesting.expectOne({ method: 'POST', url: questionsUrl }).flush(
      sampleAnswer({
        isAnswerable: false,
        outcome: 'NoDocuments',
        answer: "You don't have any processed documents yet.",
        citations: [],
      }),
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.textContent).toContain('No documents to search yet');
    expect(root.textContent).toContain("You don't have any processed documents yet.");
    expect(root.textContent).toContain('Upload a document');
  });

  it('shows the I-don-t-know message when nothing is relevant', async () => {
    const { fixture, root } = createPage();

    typeQuestion(fixture, root, 'Unrelated question');
    submit(fixture, root);

    httpTesting.expectOne({ method: 'POST', url: questionsUrl }).flush(
      sampleAnswer({
        isAnswerable: false,
        outcome: 'NoRelevantContext',
        answer: "I don't know. I couldn't find anything in your documents.",
        citations: [],
      }),
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.textContent).toContain('No answer found');
    expect(root.textContent).toContain("I don't know.");
    // The no-relevant-context panel has no upload call to action (the sidebar link is separate).
    expect(root.textContent).not.toContain('Upload a document');
  });

  it('shows an error state when the assistant is unavailable', async () => {
    const { fixture, root } = createPage();

    typeQuestion(fixture, root, 'When is it available?');
    submit(fixture, root);

    httpTesting
      .expectOne({ method: 'POST', url: questionsUrl })
      .flush(
        {
          title: 'The assistant is unavailable.',
          detail: 'The assistant took too long to respond.',
        },
        { status: 502, statusText: 'Bad Gateway' },
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'The assistant is temporarily unavailable.',
    );
  });
});
