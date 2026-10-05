import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HomePage } from './home-page';

describe('HomePage', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HomePage],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('renders the hero and links guests to sign in or register', () => {
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.textContent).toContain('Ask your documents anything');

    const links = Array.from(root.querySelectorAll('a'));
    expect(links.map((link) => link.getAttribute('href'))).toContain('/auth');
    expect(links.some((link) => (link.getAttribute('href') ?? '').includes('mode=register'))).toBe(
      true,
    );
  });
});
