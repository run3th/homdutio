import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Dialog } from '@angular/cdk/dialog';
import { of } from 'rxjs';

import { SidebarComponent } from './sidebar.component';
import { TaskService } from '../../board/task.service';

describe('SidebarComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SidebarComponent],
      providers: [
        provideRouter([]),
        // The bottom-nav FAB opens the create-task dialog and pauses board polling.
        { provide: Dialog, useValue: { open: vi.fn(() => ({ closed: of(undefined) })) } },
        { provide: TaskService, useValue: { setPaused: vi.fn() } },
      ],
    });
  });

  function render() {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the Board and Members nav items linking to /board and /members', () => {
    const el = render().nativeElement as HTMLElement;
    const items = Array.from(el.querySelectorAll<HTMLAnchorElement>('.sidebar-nav .nav-item'));

    expect(items.map((a) => a.textContent?.trim())).toEqual(['Board', 'Members']);
    expect(items.map((a) => a.getAttribute('href'))).toEqual(['/board', '/members']);
  });

  it('the bottom-nav FAB opens the create-task dialog and pauses/resumes polling', () => {
    const fixture = render();
    const dialog = TestBed.inject(Dialog) as unknown as { open: ReturnType<typeof vi.fn> };
    const tasks = TestBed.inject(TaskService) as unknown as { setPaused: ReturnType<typeof vi.fn> };

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.fab')!.click();

    expect(dialog.open).toHaveBeenCalled();
    expect(tasks.setPaused).toHaveBeenCalledWith(true);
    expect(tasks.setPaused).toHaveBeenLastCalledWith(false);
  });
});
