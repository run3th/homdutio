import { TestBed } from '@angular/core/testing';
import { DialogRef } from '@angular/cdk/dialog';

import { SystemPromptComponent } from './system-prompt.component';
import { NotificationService } from '../notification.service';
import { FlashService } from '../../shared/flash/flash.service';

describe('SystemPromptComponent', () => {
  let close: ReturnType<typeof vi.fn>;
  let grant: ReturnType<typeof vi.fn>;
  let deny: ReturnType<typeof vi.fn>;
  let show: ReturnType<typeof vi.fn>;

  function render() {
    close = vi.fn();
    grant = vi.fn();
    deny = vi.fn();
    show = vi.fn();

    TestBed.configureTestingModule({
      imports: [SystemPromptComponent],
      providers: [
        { provide: DialogRef, useValue: { close } },
        { provide: NotificationService, useValue: { grant, deny } },
        { provide: FlashService, useValue: { show } },
      ],
    });

    const fixture = TestBed.createComponent(SystemPromptComponent);
    fixture.detectChanges();
    return fixture;
  }

  function button(fixture: ReturnType<typeof render>, label: string): HTMLButtonElement {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === label,
    ) as HTMLButtonElement;
  }

  it('Allow grants, confirms with a flash, and closes with "granted"', () => {
    const fixture = render();
    button(fixture, 'Allow').click();

    expect(grant).toHaveBeenCalledOnce();
    expect(show).toHaveBeenCalledWith('Notifications on for this device');
    expect(close).toHaveBeenCalledWith('granted');
  });

  it("Don't Allow denies and closes with \"denied\"", () => {
    const fixture = render();
    button(fixture, "Don't Allow").click();

    expect(deny).toHaveBeenCalledOnce();
    expect(show).not.toHaveBeenCalled();
    expect(close).toHaveBeenCalledWith('denied');
  });
});
