import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { BreakpointObserver } from '@angular/cdk/layout';
import { Dialog } from '@angular/cdk/dialog';
import { of } from 'rxjs';

import { NotificationService, NotifPermission, RegisteredDevice } from './notification.service';
import { FlashService } from '../shared/flash/flash.service';
import { AuthService } from '../auth/auth.service';

const PERM_KEY = 'homdutio_notif_perm';
const DEVICES_KEY = 'homdutio_devices';

describe('NotificationService', () => {
  let flashPush: ReturnType<typeof vi.fn>;
  let flashShow: ReturnType<typeof vi.fn>;
  let dialogOpen: ReturnType<typeof vi.fn>;

  /** Configure a fresh service with a fixed viewport, seeded consent, and a seeded device registry. */
  function setup(opts: {
    mobile: boolean;
    permission?: NotifPermission;
    devices?: RegisteredDevice[];
    displayName?: string;
  }): NotificationService {
    localStorage.clear();
    if (opts.permission) {
      localStorage.setItem(PERM_KEY, opts.permission);
    }
    if (opts.devices) {
      localStorage.setItem(DEVICES_KEY, JSON.stringify(opts.devices));
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
        { provide: AuthService, useValue: { displayName: signal(opts.displayName ?? 'Rafał') } },
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
      expect(setup({ mobile: true, permission: 'granted' }).permission()).toBe('granted');
    });

    it('defaults to "default" when nothing is stored', () => {
      expect(setup({ mobile: true }).permission()).toBe('default');
    });

    it('grant() and deny() persist consent to localStorage', () => {
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
      setup({ mobile: true, permission: 'granted' }).pushNotify('Title', 'Body');
      expect(flashPush).toHaveBeenCalledWith('Title', 'Body');
    });

    it('does nothing when mobile but not granted', () => {
      setup({ mobile: true, permission: 'denied' }).pushNotify('Title', 'Body');
      expect(flashPush).not.toHaveBeenCalled();
    });

    it('does nothing on desktop even when granted', () => {
      setup({ mobile: false, permission: 'granted' }).pushNotify('Title', 'Body');
      expect(flashPush).not.toHaveBeenCalled();
    });
  });

  describe('requestNotifs is mobile-only and short-circuits when granted', () => {
    it('opens the prompt on mobile when consent is default', () => {
      setup({ mobile: true }).requestNotifs();
      expect(dialogOpen).toHaveBeenCalledOnce();
    });

    it('does nothing on desktop', () => {
      setup({ mobile: false }).requestNotifs();
      expect(dialogOpen).not.toHaveBeenCalled();
    });

    it('does nothing when already granted', () => {
      setup({ mobile: true, permission: 'granted' }).requestNotifs();
      expect(dialogOpen).not.toHaveBeenCalled();
    });
  });

  describe('deviceList — devices appear only once subscribed; no fabricated Off rows', () => {
    it('desktop with an empty registry shows no rows', () => {
      expect(setup({ mobile: false }).deviceList()).toEqual([]);
    });

    it('mobile with an empty registry shows a single synthetic "This phone · Off" activation row', () => {
      const list = setup({ mobile: true }).deviceList();
      expect(list).toHaveLength(1);
      expect(list[0]).toMatchObject({
        name: 'This phone',
        enabled: false,
        isCurrent: true,
        showEnable: true,
      });
    });

    it('after granting on mobile, this device is registered On (named from the display name)', () => {
      const service = setup({ mobile: true, displayName: 'Rafał' });
      service.grant();

      const list = service.deviceList();
      expect(list).toHaveLength(1);
      expect(list[0]).toMatchObject({ name: "Rafał's phone", enabled: true, isCurrent: true, showEnable: false });
      // Persisted to the account registry.
      expect(JSON.parse(localStorage.getItem(DEVICES_KEY)!)).toHaveLength(1);
    });

    it('revoking after granting flips the row to Off and re-offers enable (no extra rows)', () => {
      const service = setup({ mobile: true });
      service.grant();
      service.deny();

      const list = service.deviceList();
      expect(list).toHaveLength(1);
      expect(list[0]).toMatchObject({ enabled: false, isCurrent: true, showEnable: true });
    });

    it('denying without ever granting fabricates no registry row (still just the synthetic prompt row)', () => {
      const service = setup({ mobile: true });
      service.deny();

      expect(JSON.parse(localStorage.getItem(DEVICES_KEY) ?? '[]')).toEqual([]);
      expect(service.deviceList()).toHaveLength(1);
      expect(service.deviceList()[0].enabled).toBe(false);
    });

    it('desktop shows a registered phone (from another device) as a non-current On row', () => {
      const service = setup({
        mobile: false,
        devices: [{ id: 'other-phone', name: "Rafał's phone", enabled: true }],
      });
      const list = service.deviceList();
      expect(list).toHaveLength(1);
      expect(list[0]).toMatchObject({ name: "Rafał's phone", enabled: true, isCurrent: false, showEnable: false });
    });
  });

  describe('notifStatusText & anyEnabled', () => {
    it('reads "not on for any" for an empty registry', () => {
      const service = setup({ mobile: false });
      expect(service.notifStatusText()).toBe("Notifications aren't on for any of your devices yet.");
      expect(service.anyEnabled()).toBe(false);
    });

    it('reads a count once a device is subscribed', () => {
      const service = setup({ mobile: true });
      service.grant();
      expect(service.notifStatusText()).toBe('Notifications are on for 1 device.');
      expect(service.anyEnabled()).toBe(true);
    });
  });
});
