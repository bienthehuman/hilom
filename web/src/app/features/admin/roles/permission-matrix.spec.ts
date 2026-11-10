import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { type PermissionGroupResponse } from '../../../api/models/permission-group-response';
import { type GrantMap, PermissionMatrix, mixesPortalAndStaffAccess } from './permission-matrix';

const GROUPS: PermissionGroupResponse[] = [
  {
    group: 'Patients',
    permissions: [
      {
        key: 'patients.view',
        description: 'See demographics',
        allowedScopes: ['all', 'ward', 'own', 'linked'],
      },
      { key: 'patients.register', description: 'Register a new patient', allowedScopes: ['all'] },
    ],
  },
];

@Component({
  imports: [PermissionMatrix],
  template: `<app-permission-matrix
    [groups]="groups"
    [(grants)]="grants"
    [readOnly]="readOnly()"
  />`,
})
class Host {
  readonly groups = GROUPS;
  readonly grants = signal<GrantMap>({ 'patients.view': 'ward' });
  readonly readOnly = signal(false);
}

describe('PermissionMatrix', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const row = (key: string) => el.querySelector(`tr[data-permission="${key}"]`) as HTMLElement;
    const radio = (key: string, label: string) =>
      row(key).querySelector(`input[aria-label$=": ${label}"]`) as HTMLInputElement | null;
    return { fixture, el, row, radio, host: fixture.componentInstance };
  }

  it('offers only the scopes each permission allows', async () => {
    const { radio } = await render();

    expect(radio('patients.view', 'Ward')).not.toBeNull();
    expect(radio('patients.register', 'All')).not.toBeNull();
    expect(radio('patients.register', 'Ward')).toBeNull();
  });

  it('shows the current grants and a count per module', async () => {
    const { el, radio } = await render();

    expect(radio('patients.view', 'Ward')?.checked).toBe(true);
    expect(radio('patients.register', 'not granted')?.checked).toBe(true);
    expect(el.querySelector('caption')?.textContent).toContain('1 of 2 granted');
  });

  it('updates the grants when a scope is chosen or cleared', async () => {
    const { fixture, radio, host } = await render();

    radio('patients.register', 'All')?.click();
    radio('patients.view', 'not granted')?.click();
    await fixture.whenStable();

    expect(host.grants()).toEqual({ 'patients.register': 'all' });
  });

  it('disables every choice when read-only', async () => {
    const { fixture, el, host } = await render();

    host.readOnly.set(true);
    await fixture.whenStable();

    const radios = Array.from(el.querySelectorAll('input[type="radio"]')) as HTMLInputElement[];
    expect(radios.length).toBeGreaterThan(0);
    expect(radios.every((r) => r.disabled)).toBe(true);
  });
});

describe('mixesPortalAndStaffAccess', () => {
  it('flags roles that combine linked patients with all or ward access', () => {
    expect(
      mixesPortalAndStaffAccess({ 'patients.view': 'linked', 'clinical.vitals.record': 'ward' }),
    ).toBe(true);
    expect(
      mixesPortalAndStaffAccess({
        'patients.view': 'linked',
        'scheduling.appointments.book': 'linked',
      }),
    ).toBe(false);
    expect(mixesPortalAndStaffAccess({ 'patients.view': 'all' })).toBe(false);
  });
});
