import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TagInputComponent } from './tag-input.component';

describe('TagInputComponent', () => {
  let fixture: ComponentFixture<TagInputComponent>;
  let component: TagInputComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [TagInputComponent] });
    fixture = TestBed.createComponent(TagInputComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('adds a trimmed, whitespace-collapsed tag', () => {
    component.addTag('  multi   word ');
    expect(component.tags()).toEqual(['multi word']);
  });

  it('de-dups case-insensitively, keeping the first-seen casing', () => {
    component.addTag('Kitchen');
    component.addTag('kitchen');
    expect(component.tags()).toEqual(['Kitchen']);
  });

  it('removes a tag by index', () => {
    component.addTag('a');
    component.addTag('b');
    component.removeTag(0);
    expect(component.tags()).toEqual(['b']);
  });

  it('rejects a tag over the 50-char cap and surfaces an error', () => {
    component.addTag('x'.repeat(51));
    expect(component.tags()).toEqual([]);
    expect(component.error()).toContain('50');
  });

  it('rejects beyond the max tag count', () => {
    for (let i = 0; i < 10; i++) {
      component.addTag(`t${i}`);
    }
    component.addTag('overflow');
    expect(component.tags().length).toBe(10);
    expect(component.error()).toContain('10');
  });

  it('filters suggestions by query, excluding already-selected tags', () => {
    fixture.componentRef.setInput('suggestions', ['Kitchen', 'Garden', 'Pets']);
    component.addTag('Kitchen');
    component.query.set('e'); // Garden + Pets contain "e"; Kitchen is selected, so excluded
    expect(component.filtered()).toEqual(['Garden', 'Pets']);
  });

  it('shows a Create row only for a value not already a tag or an exact suggestion', () => {
    fixture.componentRef.setInput('suggestions', ['Kitchen']);
    component.query.set('Laundry');
    expect(component.showCreate()).toBe(true);
    component.query.set('Kitchen');
    expect(component.showCreate()).toBe(false);
  });

  it('propagates value changes through the ControlValueAccessor', () => {
    const changes: string[][] = [];
    component.registerOnChange((v) => changes.push(v));
    component.addTag('a');
    expect(changes.at(-1)).toEqual(['a']);
  });

  it('writeValue seeds the chips from a bound control', () => {
    component.writeValue(['Kitchen', 'Garden']);
    expect(component.tags()).toEqual(['Kitchen', 'Garden']);
  });
});
