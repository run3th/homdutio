import { TestBed } from '@angular/core/testing';
import { DialogRef } from '@angular/cdk/dialog';

import { DenyHelpComponent } from './deny-help.component';

describe('DenyHelpComponent', () => {
  let close: ReturnType<typeof vi.fn>;

  function render() {
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [DenyHelpComponent],
      providers: [{ provide: DialogRef, useValue: { close } }],
    });
    const fixture = TestBed.createComponent(DenyHelpComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the unblock instructions', () => {
    const el = render().nativeElement as HTMLElement;
    expect(el.textContent).toContain('Notifications are blocked');
    expect(el.querySelectorAll('.deny-help-steps li').length).toBeGreaterThan(0);
  });

  it('Got it closes the dialog', () => {
    const fixture = render();
    const button = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === 'Got it') as HTMLButtonElement;

    button.click();

    expect(close).toHaveBeenCalledOnce();
  });
});
