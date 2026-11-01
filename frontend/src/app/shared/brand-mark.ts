import { Component, input } from '@angular/core';

/** An eight-pointed star, from the login lattice, holding an open book. */
@Component({
  selector: 'app-brand-mark',
  template: `
    <svg viewBox="0 0 32 32" [attr.width]="size()" [attr.height]="size()" aria-hidden="true">
      <path
        fill="var(--brand-primary)"
        d="M16 .5l3.6 3.3 4.9-.2.2 4.9 3.3 3.6L24.7 16l3.3 3.9-3.3 3.6-.2 4.9-4.9-.2L16 31.5l-3.6-3.3-4.9.2-.2-4.9L4 19.9 7.3 16 4 12.1l3.3-3.6.2-4.9 4.9.2z"
      />
      <path fill="#fff" d="M9 11.2c2.8-.9 5.1-.6 6.4.7v9.3c-1.3-1.2-3.6-1.5-6.4-.7z" />
      <path fill="#fff" d="M23 11.2c-2.8-.9-5.1-.6-6.4.7v9.3c1.3-1.2 3.6-1.5 6.4-.7z" />
      <circle cx="16" cy="8.4" r="1.5" fill="var(--brand-accent)" />
    </svg>
  `,
  styles: ':host { display: inline-flex; }',
})
export class BrandMark {
  readonly size = input(34);
}
