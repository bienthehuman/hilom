import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';

import { AuthService } from './auth.service';

/**
 * Renders its content only when the signed-in user holds the permission. Updates live if permissions change.
 * Hides what the user can't use; the API is what actually blocks it (docs/RBAC.md).
 *
 * ```html
 * <button *appHasPermission="'access.users.manage'" hlmBtn>New user</button>
 * ```
 */
@Directive({ selector: '[appHasPermission]' })
export class HasPermission {
  private readonly auth = inject(AuthService);
  private readonly template = inject(TemplateRef);
  private readonly container = inject(ViewContainerRef);

  readonly appHasPermission = input.required<string>();

  private shown = false;

  constructor() {
    effect(() => {
      const allowed = this.auth.can(this.appHasPermission());
      if (allowed && !this.shown) {
        this.container.createEmbeddedView(this.template);
        this.shown = true;
      } else if (!allowed && this.shown) {
        this.container.clear();
        this.shown = false;
      }
    });
  }
}
