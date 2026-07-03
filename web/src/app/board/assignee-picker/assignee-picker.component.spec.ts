import { TestBed } from '@angular/core/testing';

import { AssigneePickerComponent } from './assignee-picker.component';
import { Member } from '../../household/member.service';

describe('AssigneePickerComponent', () => {
  const members: Member[] = [
    { userId: 'me', displayName: 'Rafał', email: 'r@x', role: 'Admin', isSelf: true, canManage: false },
    { userId: 'u2', displayName: 'Molly', email: 'm@x', role: 'Member', isSelf: false, canManage: true },
  ];

  function render(value = '') {
    TestBed.configureTestingModule({ imports: [AssigneePickerComponent] });
    const fixture = TestBed.createComponent(AssigneePickerComponent);
    fixture.componentRef.setInput('members', members);
    fixture.componentRef.setInput('value', value);
    fixture.detectChanges();
    return fixture;
  }

  function chips(fixture: ReturnType<typeof render>): HTMLButtonElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.assign-chip'));
  }

  it('renders a leading "Anyone" chip plus one per member, with " (you)" for self', () => {
    const labels = chips(render()).map((c) => c.querySelector('.assign-chip-name')?.textContent?.trim());
    expect(labels).toEqual(['Anyone', 'Rafał (you)', 'Molly']);
  });

  it('marks the chip matching the current value as selected', () => {
    const selected = chips(render('u2')).filter((c) => c.classList.contains('assign-chip--selected'));
    expect(selected).toHaveLength(1);
    expect(selected[0].textContent).toContain('Molly');
  });

  it('emits the member id when a chip is clicked, and "" for Anyone', () => {
    const fixture = render('u2');
    const emitted: string[] = [];
    fixture.componentInstance.valueChange.subscribe((v) => emitted.push(v));

    chips(fixture).find((c) => c.textContent?.includes('Molly'))!.click();
    chips(fixture).find((c) => c.textContent?.includes('Anyone'))!.click();

    expect(emitted).toEqual(['u2', '']);
  });
});
