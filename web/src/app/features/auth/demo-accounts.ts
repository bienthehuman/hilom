import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  inject,
  input,
  output,
  resource,
} from '@angular/core';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFlaskConical } from '@ng-icons/lucide';

import { Api } from '../../api/api';
import { listDemoAccounts } from '../../api/fn/demo/list-demo-accounts';
import { type DemoAccountResponse } from '../../api/models/demo-account-response';

/** What the page needs to sign in as a demo account: who, and the shared password the API published. */
export interface DemoLogin {
  email: string;
  password: string;
}

/**
 * One-click logins for each role, offered only when the API is running in demo mode. Outside demo mode the API
 * answers "not enabled" and this renders nothing, so a real deployment never shows it.
 */
@Component({
  selector: 'app-demo-accounts',
  imports: [NgIcon, HlmButtonImports, HlmCardImports, HlmSkeletonImports],
  providers: [provideIcons({ lucideFlaskConical })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (accounts.isLoading() && !accounts.hasValue()) {
      <div hlmCard aria-busy="true" aria-label="Loading demo logins">
        <div hlmCardContent class="flex flex-col gap-3">
          <div hlmSkeleton class="h-5 w-40"></div>
          <div hlmSkeleton class="h-12 w-full"></div>
          <div hlmSkeleton class="h-12 w-full"></div>
        </div>
      </div>
    } @else if (accounts.error()) {
      <p class="text-center text-sm text-muted-foreground" role="status">
        Demo logins couldn't be loaded.
        <button
          hlmBtn
          variant="link"
          size="sm"
          type="button"
          class="h-auto p-0"
          (click)="accounts.reload()"
        >
          Try again
        </button>
      </p>
    } @else if (visible().length > 0) {
      <section hlmCard aria-labelledby="demo-accounts-title">
        <div hlmCardContent class="flex flex-col gap-4">
          <div>
            <h2 id="demo-accounts-title" class="flex items-center gap-2 text-base font-semibold">
              <ng-icon name="lucideFlaskConical" aria-hidden="true" />
              Try the demo
            </h2>
            <p class="mt-1 text-sm text-muted-foreground">
              Everything here is made up, and it resets every night. Pick a role to sign in as them.
            </p>
          </div>
          <ul class="flex flex-col gap-2">
            @for (account of visible(); track account.email) {
              <li>
                <button
                  hlmBtn
                  variant="outline"
                  type="button"
                  class="h-auto w-full flex-col items-start gap-0.5 py-2 text-start whitespace-normal"
                  [disabled]="disabled()"
                  (click)="choose(account)"
                >
                  <span class="font-medium">{{ account.role }}</span>
                  <span class="text-sm font-normal text-muted-foreground">
                    {{ account.displayName }}: {{ account.description }}
                  </span>
                </button>
              </li>
            }
          </ul>
        </div>
      </section>
    }
  `,
})
export class DemoAccounts {
  private readonly api = inject(Api);

  /**
   * The patient portal's sign-in lists the portal account; the staff sign-in lists everyone else. The router binds a
   * missing route-data key as `undefined`, which counts as false.
   */
  readonly portal = input(false, { transform: booleanAttribute });
  /** Disabled while a sign-in is already under way. */
  readonly disabled = input(false);
  readonly signIn = output<DemoLogin>();

  protected readonly accounts = resource({ loader: () => this.api.invoke(listDemoAccounts) });

  protected readonly visible = computed<DemoAccountResponse[]>(() => {
    const result = this.accounts.hasValue() ? this.accounts.value() : null;
    if (!result?.enabled) {
      return [];
    }
    return result.accounts.filter((a) => a.portal === this.portal());
  });

  protected choose(account: DemoAccountResponse): void {
    const password = this.accounts.hasValue() ? this.accounts.value().password : null;
    if (password) {
      this.signIn.emit({ email: account.email, password });
    }
  }
}
