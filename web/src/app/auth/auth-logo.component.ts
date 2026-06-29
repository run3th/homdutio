import { Component } from '@angular/core';

/**
 * The centered Homdutio logo lockup that heads every auth card (login / register / forgot / reset). A small
 * accent house-mark beside the "Homdutio" wordmark + "Shared chores" eyebrow — the same brand lockup as the
 * app's top header, centered above the card title. Self-contained (inline template + styles) so the four
 * auth screens share one definition without duplicating markup.
 */
@Component({
  selector: 'app-auth-logo',
  template: `
    <div class="auth-logo">
      <span class="auth-logo-mark" aria-hidden="true">
        <svg viewBox="0 0 48 48" width="25" height="25" fill="none">
          <path
            d="M7 25 L24 10 L41 25"
            stroke="#fff"
            stroke-width="4"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
          <rect x="20.5" y="29" width="7" height="9" rx="2" fill="#fff" />
        </svg>
      </span>
      <span class="auth-logo-text">
        <span class="auth-logo-name">Homdutio</span>
        <span class="auth-logo-eyebrow">Shared chores</span>
      </span>
    </div>
  `,
  styles: [
    `
      .auth-logo {
        display: flex;
        align-items: center;
        justify-content: center;
        gap: 0.625rem;
        margin-bottom: var(--space-5);
      }

      .auth-logo-mark {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 2.5rem;
        height: 2.5rem;
        flex-shrink: 0;
        background: var(--color-primary);
        border-radius: var(--radius-sm);
      }

      .auth-logo-text {
        display: flex;
        flex-direction: column;
        line-height: 1.1;
      }

      .auth-logo-name {
        font-weight: var(--weight-bold);
        font-size: var(--text-lg);
        color: var(--color-text);
        letter-spacing: -0.01em;
      }

      .auth-logo-eyebrow {
        font-size: 0.625rem;
        font-weight: var(--weight-semibold);
        letter-spacing: 0.08em;
        text-transform: uppercase;
        color: var(--color-text-subtle);
      }
    `,
  ],
})
export class AuthLogoComponent {}
