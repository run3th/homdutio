import { TestBed } from '@angular/core/testing';
import { Dialog } from '@angular/cdk/dialog';
import { of, throwError } from 'rxjs';

import { MembersComponent } from './members.component';
import { Member, MemberService } from '../member.service';

describe('MembersComponent', () => {
  let list: ReturnType<typeof vi.fn>;
  let setRole: ReturnType<typeof vi.fn>;
  let remove: ReturnType<typeof vi.fn>;
  let open: ReturnType<typeof vi.fn>;

  const admin: Member = {
    userId: 'u1', displayName: 'Molly', email: 'molly@x.test', role: 'Admin', isSelf: true, canManage: false,
  };
  const member: Member = {
    userId: 'u2', displayName: 'Arthur', email: 'arthur@x.test', role: 'Member', isSelf: false, canManage: true,
  };

  beforeEach(() => {
    list = vi.fn(() => of([admin, member]));
    setRole = vi.fn(() => of({ ...member, role: 'Admin' as const })) ;
    remove = vi.fn(() => of(undefined));
    // Default: confirm dialog resolves true (a confirmed removal); individual tests override.
    open = vi.fn(() => ({ closed: of(true) }));

    TestBed.configureTestingModule({
      providers: [
        MembersComponent,
        { provide: MemberService, useValue: { list, setRole, remove } },
        { provide: Dialog, useValue: { open } },
      ],
    });
  });

  function render() {
    const fixture = TestBed.createComponent(MembersComponent);
    fixture.detectChanges(); // ngOnInit → load()
    return fixture;
  }

  it('lists every member with name, email, and role badge', () => {
    const el = render().nativeElement as HTMLElement;
    const rows = el.querySelectorAll('.member-row');

    expect(rows.length).toBe(2);
    expect(el.textContent).toContain('Molly');
    expect(el.textContent).toContain('arthur@x.test');
    expect(el.querySelectorAll('.role-badge').length).toBe(2);
  });

  it('an admin sees promote + remove on a manageable member, and no controls on their own row', () => {
    const el = render().nativeElement as HTMLElement;
    const rows = Array.from(el.querySelectorAll('.member-row'));

    const selfRow = rows.find((r) => r.textContent?.includes('Molly'))!;
    expect(selfRow.querySelector('.member-actions')).toBeNull(); // canManage = false on self

    const memberRow = rows.find((r) => r.textContent?.includes('Arthur'))!;
    const actions = memberRow.querySelector('.member-actions')!;
    expect(actions).not.toBeNull();
    expect(actions.textContent).toContain('Make admin');
    expect(actions.textContent).toContain('Remove');
  });

  it('a non-admin sees a read-only roster (no action controls anywhere)', () => {
    list = vi.fn(() =>
      of([
        { ...admin, isSelf: false, canManage: false },
        { ...member, isSelf: true, canManage: false },
      ]),
    );
    TestBed.overrideProvider(MemberService, { useValue: { list, setRole, remove } });

    const el = render().nativeElement as HTMLElement;
    expect(el.querySelectorAll('.member-actions').length).toBe(0);
  });

  it('promoting a member calls setRole and refetches the roster', () => {
    const fixture = render();
    const component = fixture.componentInstance;

    component.promote(member);

    expect(setRole).toHaveBeenCalledWith('u2', 'Admin');
    expect(list).toHaveBeenCalledTimes(2); // initial load + post-action refetch
  });

  it('a confirmed removal calls remove and refetches', () => {
    const fixture = render();

    fixture.componentInstance.remove(member);

    expect(open).toHaveBeenCalled();
    expect(remove).toHaveBeenCalledWith('u2');
    expect(list).toHaveBeenCalledTimes(2);
  });

  it('a cancelled removal does nothing', () => {
    open = vi.fn(() => ({ closed: of(false) }));
    TestBed.overrideProvider(Dialog, { useValue: { open } });

    const fixture = render();
    fixture.componentInstance.remove(member);

    expect(remove).not.toHaveBeenCalled();
    expect(list).toHaveBeenCalledTimes(1); // only the initial load
  });

  it('surfaces the server 409 message when an action is rejected', () => {
    setRole = vi.fn(() =>
      throwError(() => ({ status: 409, error: { message: 'The household must keep at least one admin.' } })),
    );
    TestBed.overrideProvider(MemberService, { useValue: { list, setRole, remove } });

    const fixture = render();
    fixture.componentInstance.demote({ ...member, role: 'Admin' });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.members-error')?.textContent).toContain('at least one admin');
  });
});
