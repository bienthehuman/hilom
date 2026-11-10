import { type LinkRelationship } from '../../api/models/link-relationship';
import { type Option } from '../../core/format/options';

/** What the account holder is to the patient, as staff read it. */
export const RELATIONSHIPS: readonly Option<LinkRelationship>[] = [
  { value: 'self', label: 'The patient themselves' },
  { value: 'parent', label: 'Parent' },
  { value: 'guardian', label: 'Legal guardian' },
  { value: 'spouse', label: 'Spouse' },
  { value: 'child', label: 'Son or daughter' },
  { value: 'representative', label: 'Representative' },
];

/** The same choices, asked of the portal user ("You are the patient's…"). */
export const RELATIONSHIPS_FOR_REQUESTER: readonly Option<LinkRelationship>[] = [
  { value: 'self', label: "I'm the patient" },
  { value: 'parent', label: 'Parent' },
  { value: 'guardian', label: 'Legal guardian' },
  { value: 'spouse', label: 'Husband or wife' },
  { value: 'child', label: 'Son or daughter' },
  { value: 'representative', label: 'Someone else who helps them' },
];

/** Sensible starting access when verifying: people acting for the patient see records; others can book. */
export function defaultLinkAccess(relationship: LinkRelationship): {
  canBook: boolean;
  canViewRecords: boolean;
} {
  return {
    canBook: true,
    canViewRecords:
      relationship === 'self' || relationship === 'parent' || relationship === 'guardian',
  };
}

/** "Can book · Can see records", or what's missing. */
export function describeAccess(link: { canBook: boolean; canViewRecords: boolean }): string {
  const parts = [
    link.canBook ? 'Can book visits' : "Can't book",
    link.canViewRecords ? 'can see records' : "can't see records",
  ];
  return parts.join(' · ');
}

/** How the portal describes the account holder's relationship ("You're their son or daughter"). */
export const RELATIONSHIP_FOR_YOU: Record<LinkRelationship, string> = {
  self: 'This is you',
  parent: "You're their parent",
  guardian: "You're their legal guardian",
  spouse: "You're their husband or wife",
  child: "You're their son or daughter",
  representative: 'You help them',
};

/** For staff: "Maria says she is {the son or daughter of} Juan Dela Cruz". */
export const RELATIONSHIP_TO: Record<LinkRelationship, string> = {
  self: 'the patient,',
  parent: 'the parent of',
  guardian: 'the legal guardian of',
  spouse: 'the spouse of',
  child: 'the son or daughter of',
  representative: 'a representative of',
};
