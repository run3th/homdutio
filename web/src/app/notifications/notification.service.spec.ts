import { TestBed } from '@angular/core/testing';
import { BreakpointObserver } from '@angular/cdk/layout';
import { Dialog } from '@angular/cdk/dialog';
import { of } from 'rxjs';

import { NotificationService, NotifPermission } from './notification.service';
import { FlashService } from '../shared/flash/flash.service';

const PERM_KEY = 'homdutio_notif_perm';

describe('NotificationService', () => {
  let flashPush: ReturnType<typeof vi.fn>;
  let flashShow: ReturnType<typeof vi.fn>;
  let dialogOpen: ReturnType<typeof vi.fn>;

  /** Configure a fresh service with a fixed viewport size and (optionally) a seeded stored consent. */
  function setup(opts: { mobile: boolean; permission?: NotifPermission }): NotificationService {
    localStorage.clear();
    if (opts.permission) {
      localStorage.setItem(PERM_KEY, opts.permission);
    }

    flashPush = vi.fn();
    flashShow = vi.fn();
    dialogOpen = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        NotificationService,
        {
          provide: BreakpointObserver,
          useValue: {
            observe: () => of({ matches: opts.mobile, breakpoints: {} }),
            isMatched: () => opts.mobile,
          },
        },
        { provide: Dialog, useValue: { open: dialogOpen } },
        { provide: FlashService, useValue: { push: flashPush, show: flashShow } },
      ],
    });

    return TestBed.inject(NotificationService);
  }

  afterEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  describe('permission localStorage round-trip', () => {
    it('initializes from a stored granted value', () => {
      const service = setup({ mobile: true, permission: 'granted' });
      expect(service.permission()).toBe('granted');
    });

    it('defaults to "default" when nothing is stored', () => {
      const service = setup({ mobile: true });
      expect(service.permission()).toBe('default');
    });

    it('grant() and deny() persist to localStorage and update the signal', () => {
      const service = setup({ mobile: true });

      service.grant();
      expect(service.permission()).toBe('granted');
      expect(localStorage.getItem(PERM_KEY)).toBe('granted');

      service.deny();
      expect(service.permission()).toBe('denied');
      expect(localStorage.getItem(PERM_KEY)).toBe('denied');
    });
  });

  describe('pushNotify isMobile && granted gate', () => {
    it('delivers when mobile and granted', () => {
      const service = setup({ mobile: true, permission: 'granted' });
      service.pushNotify('Title', 'Body');
      expect(flashPush).toHaveBeenCalledWith('Title', 'Body');
    });

    it('does nothing when mobile but not granted', () => {
      const service = setup({ mobile: true, permission: 'denied' });
      service.pushNotify('Title', 'Body');
      expect(flashPush).not.toHaveBeenCalled();
    });

    it('does nothing on desktop even when granted', () => {
      const service = setup({ mobile: false, permission: 'granted' });
      service.pushNotify('Title', 'Body');
      expect(flashPush).not.toHaveBeenCalled();
    });
  });

  describe('requestNotifs is mobile-only and short-circuits when granted', () => {
    it('opens the prompt on mobile when consent is default', () => {
      const service = setup({ mobile: true });
      service.requestNotifs();
      expect(dialogOpen).toHaveBeenCalledOnce();
    });

    it('does nothing on desktop', () => {
      const service = setup({ mobile: false });
      service.requestNotifs();
      expect(dialogOpen).not.toHaveBeenCalled();
    });

    it('does nothing when already granted', () => {
      const service = setup({ mobile: true, permission: 'granted' });
      service.requestNotifs();
      expect(dialogOpen).not.toHaveBeenCalled();
    });
  });

  describe('deviceList desktop-omit logic', () => {
    it('omits the current-device row on desktop (seeded devices only)', () => {
      const service = setup({ mobile: false });
      const list = service.deviceList();
      expect(list).toHaveLength(2);
      expect(list.some((d) => d.isCurrent)).toBe(false);
    });

    it('leads with the current phone on mobile and offers enable until granted', () => {
      const service = setup({ mobile: true });
      const list = service.deviceList();
      expect(list).toHaveLength(3);
      expect(list[0].isCurrent).toBe(true);
      expect(list[0].enabled).toBe(false);
      expect(list[0].showEnable).toBe(true);
    });

    it('marks the current phone enabled and drops its enable button once granted', () => {
      const service = setup({ mobile: true, permission: 'granted' });
      const current = service.deviceList()[0];
      expect(current.enabled).toBe(true);
      expect(current.showEnable).toBe(false);
    });
  });

  describe('notifStatusText variants', () => {
    it('reads "not on for any" when nothing is enabled', () => {
      const service = setup({ mobile: false });
      expect(service.notifStatusText()).toBe(
        "Notifications aren't on for any of your devices yet.",
      );
      expect(service.anyEnabled()).toBe(false);
    });

    it('reads a count when a device is enabled', () => {
      const service = setup({ mobile: true, permission: 'granted' });
      expect(service.notifStatusText()).toBe('Notifications are on for 1 device.');
      expect(service.anyEnabled()).toBe(true);
    });
  });
});
