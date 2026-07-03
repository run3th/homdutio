import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { Task, TaskService } from './task.service';

describe('TaskService', () => {
  let service: TaskService;
  let httpMock: HttpTestingController;

  const task: Task = {
    id: 't1',
    title: 'Take out bins',
    description: null,
    tags: [],
    status: 'ToDo',
    createdByName: 'Molly',
    claimerName: null,
    createdAtUtc: '2026-06-01T10:00:00Z',
    canClaim: true,
    canAssign: false,
    canMarkDone: false,
    canConfirm: false,
    willSelfAttest: false,
    canEdit: true,
    canDelete: true,
    canUnclaim: false,
    canSendBack: false,
    commentCount: 0,
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
    service.create({ title: 'Take out bins', tags: [] }).subscribe();

    const post = httpMock.expectOne('/api/tasks');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ title: 'Take out bins', tags: [] });
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

  it('assign POSTs /api/tasks/{id}/assign with the assignee id then refetches', () => {
    service.assign('t1', 'u2').subscribe();

    const post = httpMock.expectOne('/api/tasks/t1/assign');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ assigneeId: 'u2' });
    post.flush({ ...task, status: 'InProgress', claimerName: 'Molly' });

    httpMock.expectOne('/api/tasks').flush([{ ...task, status: 'InProgress', claimerName: 'Molly' }]);
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

  it('update PUTs /api/tasks/{id} then refetches', () => {
    service.update('t1', { title: 'Renamed', tags: [] }).subscribe();

    const put = httpMock.expectOne('/api/tasks/t1');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ title: 'Renamed', tags: [] });
    put.flush({ ...task, title: 'Renamed' });

    httpMock.expectOne('/api/tasks').flush([{ ...task, title: 'Renamed' }]);
    expect(service.current()[0].title).toBe('Renamed');
  });

  it('delete DELETEs /api/tasks/{id} then refetches', () => {
    service.delete('t1').subscribe();

    const del = httpMock.expectOne('/api/tasks/t1');
    expect(del.request.method).toBe('DELETE');
    del.flush(null);

    // The refetch returns an empty board (the task is gone).
    httpMock.expectOne('/api/tasks').flush([]);
    expect(service.current()).toEqual([]);
  });

  it('reorder PUTs /api/tasks/order with status + orderedIds then refetches', () => {
    service.reorder('ToDo', ['c', 'a', 'b']).subscribe();

    const put = httpMock.expectOne('/api/tasks/order');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ status: 'ToDo', orderedIds: ['c', 'a', 'b'] });
    put.flush(null);

    httpMock.expectOne('/api/tasks').flush([task]);
    expect(service.current()).toEqual([task]);
  });

  it('getTagSuggestions GETs /api/tasks/tags and returns the values', () => {
    let result: string[] | undefined;
    service.getTagSuggestions().subscribe((tags) => (result = tags));

    const req = httpMock.expectOne('/api/tasks/tags');
    expect(req.request.method).toBe('GET');
    req.flush(['Garden', 'Kitchen']);

    expect(result).toEqual(['Garden', 'Kitchen']);
  });

  it('clearOnLogout resets the board', () => {
    service.load().subscribe();
    httpMock.expectOne('/api/tasks').flush([task]);
    expect(service.current()).toEqual([task]);

    service.clearOnLogout();

    expect(service.current()).toEqual([]);
  });

  describe('polling (F-03)', () => {
    afterEach(() => {
      service.stopPolling();
      vi.useRealTimers();
    });

    it('an interval tick refetches the board (GET /api/tasks)', () => {
      vi.useFakeTimers();
      service.startPolling(4000);

      vi.advanceTimersByTime(4000);

      httpMock.expectOne('/api/tasks').flush([task]);
      expect(service.current()).toEqual([task]);
    });

    it('a tick while paused does not refetch', () => {
      vi.useFakeTimers();
      service.setPaused(true);
      service.startPolling(4000);

      vi.advanceTimersByTime(4000);

      httpMock.expectNone('/api/tasks');
    });

    it('a tick while the tab is hidden does not refetch', () => {
      vi.useFakeTimers();
      const spy = vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);
      service.startPolling(4000);

      vi.advanceTimersByTime(4000);

      httpMock.expectNone('/api/tasks');
      spy.mockRestore();
    });

    it('stopPolling halts further ticks', () => {
      vi.useFakeTimers();
      service.startPolling(4000);
      vi.advanceTimersByTime(4000);
      httpMock.expectOne('/api/tasks').flush([task]);

      service.stopPolling();
      vi.advanceTimersByTime(12000);

      httpMock.expectNone('/api/tasks');
    });

    it('a failed poll is swallowed and the next tick retries', () => {
      vi.useFakeTimers();
      service.startPolling(4000);

      vi.advanceTimersByTime(4000);
      httpMock.expectOne('/api/tasks').flush(null, { status: 500, statusText: 'Server Error' });

      // The stream survived the error; the next tick still fires.
      vi.advanceTimersByTime(4000);
      httpMock.expectOne('/api/tasks').flush([task]);
      expect(service.current()).toEqual([task]);
    });
  });
});
