import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';

import { NotificationService, NotifPermission } from './notification.service';

const MOBILE_UA = 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit Mobile/15E148';
const DESKTOP_UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120';

describe('NotificationService', () => {
  let requestPermission: ReturnType<typeof vi.fn>;
  let subscribe: ReturnType<typeof vi.fn>;
  let getSubscription: ReturnType<typeof vi.fn>;
  let unsubscribeCurrent: ReturnType<typeof vi.fn>;
  let httpGet: ReturnType<typeof vi.fn>;
  let httpPost: ReturnType<typeof vi.fn>;
  let httpRequest: ReturnType<typeof vi.fn>;

  const NEW_ENDPOINT = 'https://push.test/new';

  function setup(opts: {
    mobile: boolean;
    permission?: NotifPermission;
    requestResult?: NotificationPermission;
    keyEnabled?: boolean;
    devices?: { id: string; label: string | null; endpoint: string; createdAtUtc: string }[];
    existingEndpoint?: string;
  }): NotificationService {
    Object.defineProperty(navigator, 'userAgent', {
      value: opts.mobile ? MOBILE_UA : DESKTOP_UA,
      configurable: true,
    });

    unsubscribeCurrent = vi.fn(async () => true);
    const existingSub = opts.existingEndpoint
      ? { endpoint: opts.existingEndpoint, unsubscribe: unsubscribeCurrent, toJSON: () => ({}) }
      : null;
    const newSub = {
      endpoint: NEW_ENDPOINT,
      toJSON: () => ({ keys: { p256dh: 'PKEY', auth: 'AKEY' } }),
      unsubscribe: vi.fn(async () => true),
    };
    subscribe = vi.fn(async () => newSub);
    getSubscription = vi.fn(async () => existingSub);

    const registration = { pushManager: { subscribe, getSubscription } };
    Object.defineProperty(navigator, 'serviceWorker', {
      value: { ready: Promise.resolve(registration), register: vi.fn() },
      configurable: true,
    });

    requestPermission = vi.fn(async () => opts.requestResult ?? 'granted');
    vi.stubGlobal('Notification', { permission: opts.permission ?? 'default', requestPermission });
    vi.stubGlobal('PushManager', class {});

    httpGet = vi.fn((url: string) => {
      if (url === '/api/push/key') {
        const enabled = opts.keyEnabled !== false;
        return of({ publicKey: enabled ? 'QUJD' : null, enabled });
      }
      if (url === '/api/push/devices') {
        return of(opts.devices ?? []);
      }
      return of(null);
    });
    httpPost = vi.fn(() => of(null));
    httpRequest = vi.fn(() => of(null));

    TestBed.configureTestingModule({
      providers: [
        NotificationService,
        { provide: HttpClient, useValue: { get: httpGet, post: httpPost, request: httpRequest } },
      ],
    });

    return TestBed.inject(NotificationService);
  }

  afterEach(() => {
    vi.unstubAllGlobals();
    TestBed.resetTestingModule();
  });

  describe('supported / canActivate (phone-only)', () => {
    it('a phone with the push APIs can activate', () => {
      const service = setup({ mobile: true });
      expect(service.supported).toBe(true);
      expect(service.isMobile).toBe(true);
      expect(service.canActivate).toBe(true);
    });

    it('desktop is supported but cannot activate', () => {
      const service = setup({ mobile: false });
      expect(service.supported).toBe(true);
      expect(service.isMobile).toBe(false);
      expect(service.canActivate).toBe(false);
    });
  });

  describe('enable()', () => {
    it('on desktop is a no-op (never prompts or subscribes)', async () => {
      const service = setup({ mobile: false });
      await service.enable();
      expect(requestPermission).not.toHaveBeenCalled();
      expect(subscribe).not.toHaveBeenCalled();
      expect(httpPost).not.toHaveBeenCalled();
    });

    it('on a phone requests permission, subscribes, and POSTs the subscription', async () => {
      const service = setup({
        mobile: true,
        devices: [
          { id: 'd1', label: 'iPhone', endpoint: NEW_ENDPOINT, createdAtUtc: '2026-07-03T00:00:00Z' },
          { id: 'd2', label: 'Old', endpoint: 'https://push.test/other', createdAtUtc: '2026-07-02T00:00:00Z' },
        ],
      });

      await service.enable();

      expect(requestPermission).toHaveBeenCalledOnce();
      expect(subscribe).toHaveBeenCalledWith(
        expect.objectContaining({ userVisibleOnly: true, applicationServerKey: expect.any(Uint8Array) }),
      );
      expect(httpPost).toHaveBeenCalledWith('/api/push/subscribe', {
        endpoint: NEW_ENDPOINT,
        keys: { p256dh: 'PKEY', auth: 'AKEY' },
        deviceLabel: expect.any(String),
      });
      expect(service.permission()).toBe('granted');
      expect(service.hasCurrentSubscription()).toBe(true);

      // The devices list renders and flags the current endpoint.
      const rows = service.deviceList();
      expect(rows).toHaveLength(2);
      expect(rows.find((r) => r.endpoint === NEW_ENDPOINT)?.isCurrent).toBe(true);
      expect(rows.find((r) => r.endpoint === 'https://push.test/other')?.isCurrent).toBe(false);
    });

    it('on a phone stops (no subscribe) when the prompt is not granted', async () => {
      const service = setup({ mobile: true, requestResult: 'denied' });
      await service.enable();
      expect(service.permission()).toBe('denied');
      expect(subscribe).not.toHaveBeenCalled();
      expect(httpPost).not.toHaveBeenCalled();
    });

    it('on a phone degrades gracefully when the server has no VAPID key', async () => {
      const service = setup({ mobile: true, keyEnabled: false });
      await service.enable();
      expect(requestPermission).toHaveBeenCalledOnce();
      expect(subscribe).not.toHaveBeenCalled();
      expect(httpPost).not.toHaveBeenCalled();
    });
  });

  describe('disable()', () => {
    it('unsubscribes the current browser and DELETEs the row', async () => {
      const service = setup({ mobile: true, existingEndpoint: 'https://push.test/ep1' });

      await service.disable('https://push.test/ep1');

      expect(unsubscribeCurrent).toHaveBeenCalledOnce();
      expect(httpRequest).toHaveBeenCalledWith('delete', '/api/push/subscribe', {
        body: { endpoint: 'https://push.test/ep1' },
      });
    });

    it('DELETEs a remote device without touching the local subscription', async () => {
      const service = setup({ mobile: false, existingEndpoint: 'https://push.test/local' });

      await service.disable('https://push.test/remote');

      expect(unsubscribeCurrent).not.toHaveBeenCalled();
      expect(httpRequest).toHaveBeenCalledWith('delete', '/api/push/subscribe', {
        body: { endpoint: 'https://push.test/remote' },
      });
    });
  });

  describe('notifStatusText & anyEnabled', () => {
    it('reads "not on for any" for an empty registry', async () => {
      const service = setup({ mobile: false, devices: [] });
      await service.refreshDevices();
      expect(service.notifStatusText()).toBe("Notifications aren't on for any of your devices yet.");
      expect(service.anyEnabled()).toBe(false);
    });

    it('reads a count once devices are registered', async () => {
      const service = setup({
        mobile: false,
        devices: [
          { id: 'd1', label: 'iPhone', endpoint: 'https://push.test/a', createdAtUtc: '2026-07-03T00:00:00Z' },
          { id: 'd2', label: 'Pixel', endpoint: 'https://push.test/b', createdAtUtc: '2026-07-03T00:00:00Z' },
        ],
      });
      await service.refreshDevices();
      expect(service.notifStatusText()).toBe('Notifications are on for 2 devices.');
      expect(service.anyEnabled()).toBe(true);
    });
  });
});
