import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Dialog } from '@angular/cdk/dialog';

import { NotifBannerComponent } from './notif-banner.component';
import { NotificationService, NotifPermission } from '../notification.service';
import { DenyHelpComponent } from '../deny-help/deny-help.component';

describe('NotifBannerComponent', () => {
  let open: ReturnType<typeof vi.fn>;
  let enable: ReturnType<typeof vi.fn>;
  let dismissSoftAsk: ReturnType<typeof vi.fn>;
  let refreshDevices: ReturnType<typeof vi.fn>;

  function setup(opts: {
    mobile: boolean;
    permission?: NotifPermission;
    hasCurrentSubscription?: boolean;
    anyEnabled?: boolean;
    dismissed?: boolean;
  }) {
    open = vi.fn();
    enable = vi.fn();
    dismissSoftAsk = vi.fn();
    refreshDevices = vi.fn();
    const notif = {
      // A push-capable phone can activate; desktop cannot.
      canActivate: opts.mobile,
      isMobile: opts.mobile,
      permission: signal<NotifPermission>(opts.permission ?? 'default'),
      hasCurrentSubscription: signal(opts.hasCurrentSubscription ?? false),
      anyEnabled: signal(opts.anyEnabled ?? false),
      softAskDismissed: signal(opts.dismissed ?? false),
      enable,
      dismissSoftAsk,
      refreshDevices,
    };

    TestBed.configureTestingModule({
      imports: [NotifBannerComponent],
      providers: [
        { provide: NotificationService, useValue: notif },
        { provide: Dialog, useValue: { open } },
      ],
    });

    const fixture = TestBed.createComponent(NotifBannerComponent);
    fixture.detectChanges();
    return fixture;
  }

  function byText(fixture: ReturnType<typeof setup>, label: string): HTMLButtonElement | undefined {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === label,
    ) as HTMLButtonElement | undefined;
  }

  it('phone + not granted: shows the soft-ask; Enable calls enable()', () => {
    const fixture = setup({ mobile: true, permission: 'default' });
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Turn on notifications?');
    byText(fixture, 'Enable notifications')!.click();
    expect(enable).toHaveBeenCalledOnce();
  });

  it('phone + denied: shows the blocked state; How to unblock opens deny-help', () => {
    const fixture = setup({ mobile: true, permission: 'denied' });
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Notifications are blocked');
    expect(byText(fixture, 'Enable notifications')).toBeUndefined();

    byText(fixture, 'How to unblock')!.click();
    expect(open).toHaveBeenCalledWith(DenyHelpComponent);
  });

  it('phone + granted: renders nothing', () => {
    const fixture = setup({ mobile: true, permission: 'granted' });
    expect((fixture.nativeElement as HTMLElement).querySelector('.notif-banner')).toBeNull();
  });

  it('phone + already subscribed: renders nothing', () => {
    const fixture = setup({ mobile: true, permission: 'default', hasCurrentSubscription: true });
    expect((fixture.nativeElement as HTMLElement).querySelector('.notif-banner')).toBeNull();
  });

  it('desktop + nothing enabled: shows the informational banner with no activation CTA', () => {
    const fixture = setup({ mobile: false, anyEnabled: false });
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Get notified on your phone');
    expect(byText(fixture, 'Enable notifications')).toBeUndefined();
    expect(byText(fixture, 'How to unblock')).toBeUndefined();
  });

  it('desktop + something enabled: renders nothing', () => {
    const fixture = setup({ mobile: false, anyEnabled: true });
    expect((fixture.nativeElement as HTMLElement).querySelector('.notif-banner')).toBeNull();
  });

  it('dismissed: renders nothing even on a phone without consent', () => {
    const fixture = setup({ mobile: true, permission: 'default', dismissed: true });
    expect((fixture.nativeElement as HTMLElement).querySelector('.notif-banner')).toBeNull();
  });

  it('Dismiss calls dismissSoftAsk', () => {
    const fixture = setup({ mobile: true, permission: 'default' });
    const dismiss = (fixture.nativeElement as HTMLElement).querySelector(
      '.notif-banner-dismiss',
    ) as HTMLButtonElement;

    dismiss.click();
    expect(dismissSoftAsk).toHaveBeenCalledOnce();
  });
});
