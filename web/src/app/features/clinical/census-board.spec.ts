import { type CensusPatientResponse } from '../../api/models/census-patient-response';
import { formatStay, toBedTilePatient } from './census-board';

const PATIENT: CensusPatientResponse = {
  id: 'p1',
  mrn: 'P-000123',
  firstName: 'Juan',
  lastName: 'DelaCruz',
  encounterId: 'e1',
  canOpen: true,
  attendingPhysician: { id: 'd1', firstName: 'Jose', lastName: 'Santos' },
  admittedHours: 50,
  severeAllergies: ['Penicillin'],
  criticalVitals: ['heartRate', 'oxygenSaturation'],
  lastVitalsAt: '2026-11-11T02:00:00Z',
};

describe('census board', () => {
  it('counts hours on the first day, then days with admission as day 1', () => {
    expect(formatStay(0)).toBe('0 h');
    expect(formatStay(23)).toBe('23 h');
    expect(formatStay(24)).toBe('Day 2');
    expect(formatStay(50)).toBe('Day 3');
  });

  it('turns a census patient into a tile, naming critical measures', () => {
    expect(toBedTilePatient(PATIENT)).toEqual({
      name: 'DelaCruz, Juan',
      mrn: 'P-000123',
      physician: 'Dr. Jose Santos',
      stay: 'Day 3',
      severeAllergies: ['Penicillin'],
      criticalVitals: ['Heart rate', 'SpO₂'],
      link: '/clinical/encounters/e1',
    });
  });

  it('leaves out the link when the stay is out of reach', () => {
    expect(toBedTilePatient({ ...PATIENT, canOpen: false }).link).toBeNull();
  });
});
