import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { SidebarComponent } from './sidebar.component';

describe('SidebarComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SidebarComponent],
      providers: [provideRouter([])],
    });
  });

  function render() {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the Home and Members nav items linking to /board and /members', () => {
    const el = render().nativeElement as HTMLElement;
    const items = Array.from(el.querySelectorAll<HTMLAnchorElement>('.sidebar-item'));

    expect(items.map((a) => a.querySelector('.sidebar-label')?.textContent?.trim())).toEqual([
      'Home',
      'Members',
    ]);
    expect(items.map((a) => a.getAttribute('href'))).toEqual(['/board', '/members']);
  });
});
