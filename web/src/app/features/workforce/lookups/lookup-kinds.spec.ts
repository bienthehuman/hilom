import { LOOKUP_KINDS, LOOKUP_KIND_LIST, isLookupKindId } from './lookup-kinds';

describe('lookup kinds', () => {
  it('recognizes only the known lists', () => {
    expect(isLookupKindId('departments')).toBe(true);
    expect(isLookupKindId('specializations')).toBe(true);
    expect(isLookupKindId('wards')).toBe(false);
    expect(isLookupKindId('toString')).toBe(false);
    expect(isLookupKindId(undefined)).toBe(false);
  });

  it('uses each route segment as its id and calls the matching API', () => {
    for (const kind of LOOKUP_KIND_LIST) {
      expect(LOOKUP_KINDS[kind.id]).toBe(kind);
      expect(kind.list.PATH).toBe(`/api/workforce/${kind.id}`);
      expect(kind.deactivate.PATH).toBe(`/api/workforce/${kind.id}/{id}/deactivate`);
    }
  });
});
