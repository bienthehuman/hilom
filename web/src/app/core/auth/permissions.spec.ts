import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { type CurrentUserResponse } from '../../api/models/current-user-response';
import { navigation, visibleNavigation } from '../../layout/navigation';
import { permissionGuard } from './auth.guards';
import { AuthService } from './auth.service';
import { HasPermission } from './has-permission.directive';

const tick = () => new Promise((resolve) => setTimeout(resolve));

async function signInWith(permissions: CurrentUserResponse['permissions']): Promise<AuthService> {
  const auth = TestBed.inject(AuthService);
  const http = TestBed.inject(HttpTestingController);
  const done = auth.login('a@hilom.test', 'x');
  await tick();
  http
    .expectOne('/api/access/login')
    .flush({ id: '1', email: 'a@hilom.test', displayName: 'A', permissions });
  await done;
  return auth;
}

function setup() {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  });
}

describe('AuthService.can', () => {
  beforeEach(setup);

  it('is true for held permissions at any scope', async () => {
    const auth = await signInWith({ 'patients.view': ['ward'] });

    expect(auth.can('patients.view')).toBe(true);
    expect(auth.scopes('patients.view')).toEqual(['ward']);
    expect(auth.can('access.users.view')).toBe(false);
  });
});

@Component({
  imports: [HasPermission],
  template: `<button *appHasPermission="'access.users.manage'">New user</button>`,
})
class Host {}

describe('HasPermission directive', () => {
  beforeEach(setup);

  it('shows content only with the permission, and hides it again when the permission goes away', async () => {
    const auth = await signInWith({ 'access.users.manage': ['all'] });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('New user');

    auth.sessionEnded();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).not.toContain('New user');
  });
});

describe('permissionGuard', () => {
  beforeEach(setup);

  it('sends users without the permission to the no-access page', async () => {
    await signInWith({ 'access.roles.view': ['all'] });
    const router = TestBed.inject(Router);
    const run = (permission: string) =>
      TestBed.runInInjectionContext(() => permissionGuard(permission)({} as never, {} as never));

    expect(run('access.roles.view')).toBe(true);
    expect(router.serializeUrl(run('access.users.view') as never)).toBe('/forbidden');
  });
});

describe('visibleNavigation', () => {
  it('drops items the user may not open, and sections left empty', () => {
    const sections = visibleNavigation(navigation(), (permission) =>
      permission === 'access.roles.view' ? ['all'] : [],
    );

    const labels = sections.flatMap((s) => s.items.map((i) => i.label));
    expect(labels).toContain('Home');
    expect(labels).toContain('Roles');
    expect(labels).not.toContain('Users');
  });

  it('hides the Administration section entirely without admin permissions', () => {
    const sections = visibleNavigation(navigation(), () => []);

    expect(sections.map((s) => s.label)).not.toContain('Administration');
  });

  it('shows the ward roster only to people who can see a whole ward', () => {
    const labelsWith = (scopes: ('all' | 'ward' | 'own')[]) =>
      visibleNavigation(navigation(), (permission) =>
        permission === 'workforce.roster.view' ? scopes : [],
      ).flatMap((s) => s.items.map((i) => i.label));

    expect(labelsWith(['ward'])).toEqual(expect.arrayContaining(['Duty roster', 'My schedule']));
    expect(labelsWith(['own'])).toContain('My schedule');
    expect(labelsWith(['own'])).not.toContain('Duty roster');
  });
});
