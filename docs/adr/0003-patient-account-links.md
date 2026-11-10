# ADR 0003: Optional, many-to-many, staff-verified patient accounts

**Status:** Accepted · **Date:** 2026-11-09

## Context
Many patients aren't comfortable with technology. Some want no account. Others want a family member or representative to handle things. A family member may manage several patients, and a patient may have several family members helping.

## Decision
- A patient record never requires an account.
- `patient_account_links` connects users and patients many-to-many, with a `relationship` and two flags: `can_book`, `can_view_records`.
- A link starts as `pending`. It only becomes `active` after staff with `patients.links.manage` verify it, normally front desk checking ID.
- At most one active `self` link per patient.
- Portal permissions use the `linked` scope. Actions taken for a patient are audited with `on_behalf_of_patient_id`.

## Alternatives considered
- **`patients.user_id` (one-to-one):** what wardlink did. Can't model representatives.
- **Self-service linking by MRN + birth date:** convenient, but those details are easy to know about a relative or neighbor. Too weak for medical records.

## Consequences
- Front desk has a "pending links" queue.
- Revoking a link removes access on the next request.
- **Requests describe the patient; staff match the record** (added in slice 2.4). A request holds the name, date of birth and optional MRN as the requester typed them, and the front desk picks the patient record when verifying. The API never answers "does this patient exist?", so requesting can't be used to probe the registry.
- `relationship` is what the account holder is to the patient (a daughter linking her father is `child`).
