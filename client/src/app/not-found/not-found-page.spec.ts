import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NotFoundPage } from './not-found-page';

describe('NotFoundPage', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NotFoundPage],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('explains the page is missing and links back home', () => {
    const fixture = TestBed.createComponent(NotFoundPage);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.textContent).toContain('Page not found');

    const links = Array.from(root.querySelectorAll('a'));
    expect(links.map((link) => link.getAttribute('href'))).toContain('/');
  });
});
