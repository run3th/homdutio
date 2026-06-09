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

  it('renders the Home and Tasks nav items, both linking to /board', () => {
    const el = render().nativeElement as HTMLElement;
    const items = Array.from(el.querySelectorAll<HTMLAnchorElement>('.sidebar-item'));

    expect(items.map((a) => a.querySelector('.sidebar-label')?.textContent?.trim())).toEqual([
      'Home',
      'Tasks',
    ]);
    expect(items.map((a) => a.getAttribute('href'))).toEqual(['/board', '/board']);
  });
});
