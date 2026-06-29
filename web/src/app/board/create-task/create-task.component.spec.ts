import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { DialogRef } from '@angular/cdk/dialog';
import { of, throwError } from 'rxjs';

import { CreateTaskComponent } from './create-task.component';
import { TaskService } from '../task.service';

describe('CreateTaskComponent', () => {
  let create: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;
  let getTagSuggestions: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    create = vi.fn();
    close = vi.fn();
    getTagSuggestions = vi.fn(() => of([]));
    TestBed.configureTestingModule({
      imports: [CreateTaskComponent],
      providers: [
        { provide: TaskService, useValue: { create, getTagSuggestions } },
        { provide: DialogRef, useValue: { close } },
      ],
    });
  });

  function instance() {
    const fixture = TestBed.createComponent(CreateTaskComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('blocks submit and does not call create when the title is empty', () => {
    const component = instance();
    component.submit();
    expect(create).not.toHaveBeenCalled();
  });

  it('creates the task (trimmed, optionals omitted when blank) and closes the dialog on success', () => {
    create.mockReturnValue(of([]));
    const component = instance();
    component.form.setValue({ title: '  Take out bins  ', description: '', tags: [] });

    component.submit();

    expect(create).toHaveBeenCalledWith({
      title: 'Take out bins',
      description: undefined,
      tags: [],
    });
    expect(close).toHaveBeenCalled();
  });

  it('passes through description and tags when provided', () => {
    create.mockReturnValue(of([]));
    const component = instance();
    component.form.setValue({ title: 'Mow lawn', description: 'Front only', tags: ['Garden'] });

    component.submit();

    expect(create).toHaveBeenCalledWith({
      title: 'Mow lawn',
      description: 'Front only',
      tags: ['Garden'],
    });
  });

  it('maps 400 validation messages and keeps the dialog open', () => {
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { Title: ['A task title is required.'] } },
          }),
      ),
    );
    const component = instance();
    component.form.setValue({ title: 'x', description: '', tags: [] });

    component.submit();

    expect(component.errors()).toEqual(['A task title is required.']);
    expect(close).not.toHaveBeenCalled();
  });

  it('Cancel closes the dialog without creating', () => {
    const component = instance();
    component.close();
    expect(create).not.toHaveBeenCalled();
    expect(close).toHaveBeenCalled();
  });
});
