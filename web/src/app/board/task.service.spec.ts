import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';

import { Task, TaskService } from './task.service';

describe('TaskService', () => {
  let service: TaskService;
  let httpMock: HttpTestingController;

  const task: Task = {
    id: 't1',
    title: 'Take out bins',
    description: null,
    category: null,
    status: 'ToDo',
    createdByName: 'Molly',
    claimerName: null,
    createdAtUtc: '2026-06-01T10:00:00Z',
    canClaim: true,
    canMarkDone: false,
    canConfirm: false,
    willSelfAttest: false,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [TaskService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TaskService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('starts with an empty board', () => {
    expect(service.current()).toEqual([]);
  });

  it('load GETs /api/tasks and sets the signal', () => {
    service.load().subscribe();

    const req = httpMock.expectOne('/api/tasks');
    expect(req.request.method).toBe('GET');
    req.flush([task]);

    expect(service.current()).toEqual([task]);
  });

  it('create POSTs the task then refetches the board', () => {
    service.create({ title: 'Take out bins' }).subscribe();

    const post = httpMock.expectOne('/api/tasks');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ title: 'Take out bins' });
    post.flush(task);

    // The create chains into a load() refetch.
    const get = httpMock.expectOne('/api/tasks');
    expect(get.request.method).toBe('GET');
    get.flush([task]);

    expect(service.current()).toEqual([task]);
  });

  it('claim POSTs the claim route then refetches', () => {
    service.claim('t1').subscribe();

    const post = httpMock.expectOne('/api/tasks/t1/claim');
    expect(post.request.method).toBe('POST');
    post.flush({ ...task, status: 'InProgress' });

    httpMock.expectOne('/api/tasks').flush([{ ...task, status: 'InProgress' }]);
    expect(service.current()[0].status).toBe('InProgress');
  });

  it('markDone POSTs the done route then refetches', () => {
    service.markDone('t1').subscribe();

    const post = httpMock.expectOne('/api/tasks/t1/done');
    expect(post.request.method).toBe('POST');
    post.flush({ ...task, status: 'Done' });

    httpMock.expectOne('/api/tasks').flush([{ ...task, status: 'Done' }]);
    expect(service.current()[0].status).toBe('Done');
  });

  it('confirm POSTs the confirm route then refetches (closed task drops off)', () => {
    // Seed a board with the task, then confirm it; the refetch returns an empty board.
    service.load().subscribe();
    httpMock.expectOne('/api/tasks').flush([task]);

    service.confirm('t1').subscribe();

    const post = httpMock.expectOne('/api/tasks/t1/confirm');
    expect(post.request.method).toBe('POST');
    post.flush({ ...task, status: 'Done' });

    httpMock.expectOne('/api/tasks').flush([]);
    expect(service.current()).toEqual([]);
  });

  it('clearOnLogout resets the board', () => {
    service.load().subscribe();
    httpMock.expectOne('/api/tasks').flush([task]);
    expect(service.current()).toEqual([task]);

    service.clearOnLogout();

    expect(service.current()).toEqual([]);
  });
});
