import { formatPatientName } from './patient-labels';

describe('formatPatientName', () => {
  it('puts the last name first and keeps the middle name', () => {
    expect(
      formatPatientName({ firstName: 'Juan', middleName: 'Santos', lastName: 'Dela Cruz' }),
    ).toBe('Dela Cruz, Juan Santos');
    expect(formatPatientName({ firstName: 'Ana', middleName: null, lastName: 'Reyes' })).toBe(
      'Reyes, Ana',
    );
  });
});
