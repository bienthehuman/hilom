import { defaultLinkAccess, describeAccess } from './link-labels';

describe('link labels', () => {
  it('lets the patient, parents and guardians see records by default', () => {
    expect(defaultLinkAccess('self')).toEqual({ canBook: true, canViewRecords: true });
    expect(defaultLinkAccess('guardian')).toEqual({ canBook: true, canViewRecords: true });
    expect(defaultLinkAccess('representative')).toEqual({ canBook: true, canViewRecords: false });
  });

  it('describes what a link allows', () => {
    expect(describeAccess({ canBook: true, canViewRecords: false })).toBe(
      "Can book visits · can't see records",
    );
  });
});
