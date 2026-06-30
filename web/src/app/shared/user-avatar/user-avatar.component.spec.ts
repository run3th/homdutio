import { TestBed } from '@angular/core/testing';

import { UserAvatarComponent } from './user-avatar.component';

describe('UserAvatarComponent', () => {
  function render(inputs: { name?: string | null; avatarUrl?: string | null; size?: 'sm' | 'md' | 'lg' | number }) {
    TestBed.configureTestingModule({ imports: [UserAvatarComponent] });
    const fixture = TestBed.createComponent(UserAvatarComponent);
    if ('name' in inputs) fixture.componentRef.setInput('name', inputs.name);
    if ('avatarUrl' in inputs) fixture.componentRef.setInput('avatarUrl', inputs.avatarUrl);
    if ('size' in inputs) fixture.componentRef.setInput('size', inputs.size);
    fixture.detectChanges();
    return fixture;
  }

  it('renders an <img> with the avatar URL when one is provided', () => {
    const el = render({ name: 'Molly', avatarUrl: '/api/users/u1/avatar?v=2' }).nativeElement as HTMLElement;
    const img = el.querySelector('img') as HTMLImageElement;
    expect(img).not.toBeNull();
    expect(img.getAttribute('src')).toBe('/api/users/u1/avatar?v=2');
    expect(el.querySelector('.user-avatar__initial')).toBeNull();
  });

  it('renders the colored initial when no avatar URL is provided', () => {
    const el = render({ name: 'Molly', avatarUrl: null }).nativeElement as HTMLElement;
    expect(el.querySelector('img')).toBeNull();
    const glyph = el.querySelector('.user-avatar__initial') as HTMLElement;
    expect(glyph.textContent?.trim()).toBe('M');
  });

  it('falls back to the initial when the image fails to load', () => {
    const fixture = render({ name: 'Molly', avatarUrl: '/api/users/u1/avatar?v=2' });
    const el = fixture.nativeElement as HTMLElement;
    const img = el.querySelector('img') as HTMLImageElement;
    img.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(el.querySelector('img')).toBeNull();
    expect(el.querySelector('.user-avatar__initial')?.textContent?.trim()).toBe('M');
  });

  it('uses "?" as the initial when the name is missing', () => {
    const el = render({ name: null, avatarUrl: null }).nativeElement as HTMLElement;
    expect(el.querySelector('.user-avatar__initial')?.textContent?.trim()).toBe('?');
  });
});
