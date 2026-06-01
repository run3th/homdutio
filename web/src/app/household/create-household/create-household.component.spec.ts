import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { CreateHouseholdComponent } from './create-household.component';
import { HouseholdService } from '../household.service';

describe('CreateHouseholdComponent', () => {
  let create: ReturnType<typeof vi.fn>;
  let navSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    create = vi.fn();
    TestBed.configureTestingModule({
      imports: [CreateHouseholdComponent],
      providers: [provideRouter([]), { provide: HouseholdService, useValue: { create } }],
    });
    navSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  function instance() {
    const fixture = TestBed.createComponent(CreateHouseholdComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('blocks submit and does not call create when the name is empty', () => {
    const component = instance();
    component.submit();
    expect(create).not.toHaveBeenCalled();
  });

  it('creates the household and navigates to /board on success', () => {
    create.mockReturnValue(of({ id: 'h1', name: 'The Burrow', role: 'Admin' }));
    const component = instance();
    component.form.setValue({ name: 'The Burrow' });

    component.submit();

    expect(create).toHaveBeenCalledWith('The Burrow');
    expect(navSpy).toHaveBeenCalledWith(['/board']);
  });

  it('redirects to /board on a 409 (already in a household)', () => {
    create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
    const component = instance();
    component.form.setValue({ name: 'Second' });

    component.submit();

    expect(navSpy).toHaveBeenCalledWith(['/board']);
    expect(component.errors()).toEqual([]);
  });

  it('maps 400 validation messages without navigating', () => {
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { Name: ['A household name is required.'] } },
          }),
      ),
    );
    const component = instance();
    component.form.setValue({ name: 'x' });

    component.submit();

    expect(component.errors()).toEqual(['A household name is required.']);
    expect(navSpy).not.toHaveBeenCalled();
  });
});
