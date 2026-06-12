import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';

import { Member, MemberService } from './member.service';

describe('MemberService', () => {
  let service: MemberService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [MemberService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(MemberService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list GETs the members endpoint and returns the roster', () => {
    const roster: Member[] = [
      { userId: 'u1', displayName: 'Molly', email: 'm@x.test', role: 'Admin', isSelf: true, canManage: false },
      { userId: 'u2', displayName: 'Arthur', email: 'a@x.test', role: 'Member', isSelf: false, canManage: true },
    ];
    let result: Member[] | undefined;
    service.list().subscribe((r) => (result = r));

    const req = httpMock.expectOne('/api/households/members');
    expect(req.request.method).toBe('GET');
    req.flush(roster);

    expect(result).toEqual(roster);
  });

  it('setRole POSTs the role to the member route and returns the updated row', () => {
    const updated: Member = {
      userId: 'u2', displayName: 'Arthur', email: 'a@x.test', role: 'Admin', isSelf: false, canManage: true,
    };
    let result: Member | undefined;
    service.setRole('u2', 'Admin').subscribe((r) => (result = r));

    const req = httpMock.expectOne('/api/households/members/u2/role');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ role: 'Admin' });
    req.flush(updated);

    expect(result).toEqual(updated);
  });

  it('remove DELETEs the member route', () => {
    let completed = false;
    service.remove('u2').subscribe(() => (completed = true));

    const req = httpMock.expectOne('/api/households/members/u2');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    expect(completed).toBe(true);
  });

  it('propagates a 409 (last-admin / self-action) as an error the page surfaces', () => {
    let status: number | undefined;
    service.remove('u1').subscribe({ error: (e) => (status = e.status) });

    httpMock
      .expectOne('/api/households/members/u1')
      .flush({ message: 'You cannot remove yourself.' }, { status: 409, statusText: 'Conflict' });

    expect(status).toBe(409);
  });
});
