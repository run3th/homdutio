import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';

import { Household, HouseholdService } from './household.service';

describe('HouseholdService', () => {
  let service: HouseholdService;
  let httpMock: HttpTestingController;

  const household: Household = { id: 'h1', name: 'The Burrow', role: 'Admin' };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [HouseholdService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(HouseholdService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('starts with no household and unloaded', () => {
    expect(service.current()).toBeNull();
    expect(service.loaded()).toBe(false);
  });

  it('loadMine maps an empty 204 body to null and marks loaded', () => {
    let result: Household | null = household;
    service.loadMine().subscribe((h) => (result = h));

    const req = httpMock.expectOne('/api/households/me');
    expect(req.request.method).toBe('GET');
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(result).toBeNull();
    expect(service.current()).toBeNull();
    expect(service.loaded()).toBe(true);
  });

  it('loadMine caches: a second call does not re-hit the network', () => {
    service.loadMine().subscribe();
    httpMock.expectOne('/api/households/me').flush(household);
    expect(service.current()).toEqual(household);

    let cached: Household | null = null;
    service.loadMine().subscribe((h) => (cached = h));

    // No second request is issued; afterEach verify() would fail if one were outstanding.
    expect(cached).toEqual(household);
  });

  it('create posts the name and caches the returned household', () => {
    let created: Household | undefined;
    service.create('The Burrow').subscribe((h) => (created = h));

    const req = httpMock.expectOne('/api/households');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'The Burrow' });
    req.flush(household);

    expect(created).toEqual(household);
    expect(service.current()).toEqual(household);
    expect(service.loaded()).toBe(true);
  });

  it('clearOnLogout resets both the household and the loaded flag', () => {
    service.loadMine().subscribe();
    httpMock.expectOne('/api/households/me').flush(household);

    service.clearOnLogout();

    expect(service.current()).toBeNull();
    expect(service.loaded()).toBe(false);
  });
});
