import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { CreateTaskComponent } from './create-task.component';
import { TaskService } from '../task.service';

describe('CreateTaskComponent', () => {
  let create: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    create = vi.fn();
    TestBed.configureTestingModule({
      imports: [CreateTaskComponent],
      providers: [{ provide: TaskService, useValue: { create } }],
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

  it('creates the task (trimmed, optionals omitted when blank) and resets the form on success', () => {
    create.mockReturnValue(of([]));
    const component = instance();
    component.form.setValue({ title: '  Take out bins  ', description: '', category: '' });

    component.submit();

    expect(create).toHaveBeenCalledWith({
      title: 'Take out bins',
      description: undefined,
      category: undefined,
    });
    expect(component.form.controls.title.value).toBe('');
  });

  it('passes through description and category when provided', () => {
    create.mockReturnValue(of([]));
    const component = instance();
    component.form.setValue({ title: 'Mow lawn', description: 'Front only', category: 'Garden' });

    component.submit();

    expect(create).toHaveBeenCalledWith({
      title: 'Mow lawn',
      description: 'Front only',
      category: 'Garden',
    });
  });

  it('maps 400 validation messages without resetting the form', () => {
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
    component.form.setValue({ title: 'x', description: '', category: '' });

    component.submit();

    expect(component.errors()).toEqual(['A task title is required.']);
  });
});
