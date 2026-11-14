import { Injectable, computed, inject, signal } from '@angular/core';

import { Api } from '../../api/api';
import { changeMyPassword } from '../../api/fn/access/change-my-password';
import { getAntiforgeryToken } from '../../api/fn/access/get-antiforgery-token';
import { getCurrentUser } from '../../api/fn/access/get-current-user';
import { login } from '../../api/fn/access/login';
import { logout } from '../../api/fn/access/logout';
import { registerPortalAccount } from '../../api/fn/access/register-portal-account';
import { type CurrentUserResponse } from '../../api/models/current-user-response';
import { type PermissionScope } from '../../api/models/permission-scope';

export type AuthStatus = 'unknown' | 'authenticated' | 'anonymous';

/**
 * Who is signed in. The session itself lives in an HttpOnly cookie the browser sends automatically;
 * this service only mirrors what the API says about it.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(Api);

  private readonly _user = signal<CurrentUserResponse | null>(null);
  private readonly _status = signal<AuthStatus>('unknown');

  readonly user = this._user.asReadonly();
  readonly status = this._status.asReadonly();
  readonly isAuthenticated = computed(() => this._status() === 'authenticated');
  /** Patients and families use the portal; everyone else the staff app. */
  readonly isPortal = computed(() => this._user()?.accountType === 'portal');
  /** An administrator set the password; the API refuses almost everything until the user picks their own. */
  readonly mustChangePassword = computed(() => this._user()?.mustChangePassword === true);

  /**
   * Whether the signed-in user holds a permission (at any scope). Reading it in a template or computed
   * re-evaluates when the user changes. For showing and hiding UI only; the API enforces access.
   */
  can(permission: string): boolean {
    return (this._user()?.permissions[permission]?.length ?? 0) > 0;
  }

  /** Scopes the user holds for a permission, widest first. Empty when not granted. */
  scopes(permission: string): readonly PermissionScope[] {
    return this._user()?.permissions[permission] ?? [];
  }

  /** On startup: get an antiforgery token, then find out whether a session already exists. */
  async initialize(): Promise<void> {
    await this.refreshAntiforgeryToken();
    await this.refresh();
  }

  async refreshAntiforgeryToken(): Promise<void> {
    try {
      await this.api.invoke(getAntiforgeryToken);
    } catch {
      // The API may be down; sign-in will show the error when it's tried.
    }
  }

  async refresh(): Promise<void> {
    try {
      this.setUser(await this.api.invoke(getCurrentUser));
    } catch {
      this.clear();
    }
  }

  /** Signs in. Throws the API error (e.g. invalid_credentials, account_locked) for the page to show. */
  async login(email: string, password: string): Promise<void> {
    this.setUser(await this.api.invoke(login, { body: { email, password } }));
  }

  /** Creates a patient portal account and signs in. Throws the API error (e.g. email_taken) for the page. */
  async register(displayName: string, email: string, password: string): Promise<void> {
    this.setUser(
      await this.api.invoke(registerPortalAccount, { body: { displayName, email, password } }),
    );
  }

  /**
   * Replaces the signed-in user's password. Other sessions end; this one continues. Throws the API error
   * (e.g. wrong_current_password) for the page to show.
   */
  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    this.setUser(
      await this.api.invoke(changeMyPassword, { body: { currentPassword, newPassword } }),
    );
  }

  async logout(): Promise<void> {
    try {
      await this.api.invoke(logout);
    } finally {
      this.clear();
    }
  }

  /** The API reported the session is gone (expired, disabled, signed out elsewhere). */
  sessionEnded(): void {
    this.clear();
  }

  private setUser(user: CurrentUserResponse): void {
    this._user.set(user);
    this._status.set('authenticated');
  }

  private clear(): void {
    this._user.set(null);
    this._status.set('anonymous');
  }
}
