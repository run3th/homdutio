import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';

import { buildJoinUrl, InviteService } from './invite.service';
import { Household } from './household.service';

describe('InviteService', () => {
  let service: InviteService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [InviteService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(InviteService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('generate POSTs to the invites endpoint and returns the token + expiry', () => {
    const body = { token: 'abc123', expiresAtUtc: '2026-06-09T00:00:00Z' };
    let result: typeof body | undefined;
    service.generate().subscribe((r) => (result = r));

    const req = httpMock.expectOne('/api/households/invites');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({}); // copy-link path sends no recipient
    req.flush(body);

    expect(result).toEqual(body);
  });

  it('generate with a recipient email passes it in the POST body', () => {
    service.generate('joiner@example.com').subscribe();

    const req = httpMock.expectOne('/api/households/invites');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ recipientEmail: 'joiner@example.com' });
    req.flush({ token: 'abc123', expiresAtUtc: '2026-06-09T00:00:00Z' });
  });

  it('preview GETs the token route and returns the household name', () => {
    let result: { householdName: string } | undefined;
    service.preview('abc123').subscribe((r) => (result = r));

    const req = httpMock.expectOne('/api/households/invites/abc123');
    expect(req.request.method).toBe('GET');
    req.flush({ householdName: 'The Burrow' });

    expect(result).toEqual({ householdName: 'The Burrow' });
  });

  it('preview propagates a 410 as an error the caller branches on', () => {
    let status: number | undefined;
    service.preview('gone').subscribe({ error: (e) => (status = e.status) });

    httpMock
      .expectOne('/api/households/invites/gone')
      .flush(null, { status: 410, statusText: 'Gone' });

    expect(status).toBe(410);
  });

  it('accept POSTs to the accept route and returns the joined household', () => {
    const household: Household = { id: 'h1', name: 'The Burrow', role: 'Member' };
    let result: Household | undefined;
    service.accept('abc123').subscribe((r) => (result = r));

    const req = httpMock.expectOne('/api/households/invites/abc123/accept');
    expect(req.request.method).toBe('POST');
    req.flush(household);

    expect(result).toEqual(household);
  });

  it('buildJoinUrl composes /join/<token> against the origin', () => {
    expect(buildJoinUrl('https://app.example', 'abc123')).toBe('https://app.example/join/abc123');
  });
});
