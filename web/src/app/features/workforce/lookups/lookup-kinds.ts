import { type HttpClient, type HttpContext } from '@angular/common/http';
import { type Observable } from 'rxjs';

import { activateDepartment } from '../../../api/fn/workforce/activate-department';
import { activatePosition } from '../../../api/fn/workforce/activate-position';
import { activateSpecialization } from '../../../api/fn/workforce/activate-specialization';
import { createDepartment } from '../../../api/fn/workforce/create-department';
import { createPosition } from '../../../api/fn/workforce/create-position';
import { createSpecialization } from '../../../api/fn/workforce/create-specialization';
import { deactivateDepartment } from '../../../api/fn/workforce/deactivate-department';
import { deactivatePosition } from '../../../api/fn/workforce/deactivate-position';
import { deactivateSpecialization } from '../../../api/fn/workforce/deactivate-specialization';
import { listDepartments } from '../../../api/fn/workforce/list-departments';
import { listPositions } from '../../../api/fn/workforce/list-positions';
import { listSpecializations } from '../../../api/fn/workforce/list-specializations';
import { updateDepartment } from '../../../api/fn/workforce/update-department';
import { updatePosition } from '../../../api/fn/workforce/update-position';
import { updateSpecialization } from '../../../api/fn/workforce/update-specialization';
import { type CreateLookupRequest } from '../../../api/models/create-lookup-request';
import { type LookupResponse } from '../../../api/models/lookup-response';
import { type LookupStatusFilter } from '../../../api/models/lookup-status-filter';
import { type PagedResponseOfLookupResponse } from '../../../api/models/paged-response-of-lookup-response';
import { type UpdateLookupRequest } from '../../../api/models/update-lookup-request';
import { type StrictHttpResponse } from '../../../api/strict-http-response';

/** A generated API function, as `Api.invoke` accepts it. */
type ApiFn<P, R> = ((
  http: HttpClient,
  rootUrl: string,
  params: P,
  context?: HttpContext,
) => Observable<StrictHttpResponse<R>>) & { PATH: string };

interface ListParams {
  page?: number;
  pageSize?: number;
  sort?: string;
  q?: string;
  status?: LookupStatusFilter;
}

/** Everything the shared list screen needs to show and edit one lookup. */
export interface LookupKind {
  /** Route segment, e.g. `departments`. */
  id: LookupKindId;
  /** Title case plural, e.g. "Departments". */
  plural: string;
  /** Title case singular, e.g. "Department". */
  singular: string;
  description: string;
  /** Example shown as the name placeholder. */
  example: string;
  list: ApiFn<ListParams | undefined, PagedResponseOfLookupResponse>;
  create: ApiFn<{ body: CreateLookupRequest }, LookupResponse>;
  update: ApiFn<{ id: string; body: UpdateLookupRequest }, LookupResponse>;
  activate: ApiFn<{ id: string }, void>;
  deactivate: ApiFn<{ id: string }, void>;
}

export type LookupKindId = 'departments' | 'positions' | 'specializations';

export const LOOKUP_KINDS: Record<LookupKindId, LookupKind> = {
  departments: {
    id: 'departments',
    plural: 'Departments',
    singular: 'Department',
    description: 'Where employees work. Wards can belong to a department too.',
    example: 'Nursing Service',
    list: listDepartments,
    create: createDepartment,
    update: updateDepartment,
    activate: activateDepartment,
    deactivate: deactivateDepartment,
  },
  positions: {
    id: 'positions',
    plural: 'Positions',
    singular: 'Position',
    description: 'Job titles, such as Staff Nurse or Consultant.',
    example: 'Staff Nurse',
    list: listPositions,
    create: createPosition,
    update: updatePosition,
    activate: activatePosition,
    deactivate: deactivatePosition,
  },
  specializations: {
    id: 'specializations',
    plural: 'Specializations',
    singular: 'Specialization',
    description: "A physician's field. Patients pick a doctor by specialization when booking.",
    example: 'Cardiology',
    list: listSpecializations,
    create: createSpecialization,
    update: updateSpecialization,
    activate: activateSpecialization,
    deactivate: deactivateSpecialization,
  },
};

export const LOOKUP_KIND_LIST: readonly LookupKind[] = Object.values(LOOKUP_KINDS);

export function isLookupKindId(value: string | undefined): value is LookupKindId {
  return value !== undefined && Object.hasOwn(LOOKUP_KINDS, value);
}
