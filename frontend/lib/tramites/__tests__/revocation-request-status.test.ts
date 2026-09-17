// HU #12575 (Feature #12565, AC1) — sub-estado ACTIVO de revocatoria: helpers de badge secundario,
// mismo precedente que plate-flow-status.test.ts.
import { describe, expect, it } from 'vitest';
import {
  ESTADOS_TRAMITE,
  esRevocationRequestStatus,
  REVOCATION_REQUEST_LABELS,
  revocationRequestLabel,
  revocationRequestTone,
} from '../estados';

describe('revocation-request status', () => {
  it('solicitada/en_revision NO son estados del trámite', () => {
    expect(ESTADOS_TRAMITE).not.toContain('solicitada' as never);
    expect(ESTADOS_TRAMITE).not.toContain('en_revision' as never);
  });

  it('reconoce los sub-estados ACTIVOS válidos', () => {
    expect(esRevocationRequestStatus('solicitada')).toBe(true);
    expect(esRevocationRequestStatus('en_revision')).toBe(true);
    expect(esRevocationRequestStatus('aprobada')).toBe(false);
    expect(esRevocationRequestStatus('rechazada')).toBe(false);
    expect(esRevocationRequestStatus(null)).toBe(false);
  });

  it('devuelve labels UI', () => {
    expect(revocationRequestLabel('solicitada')).toBe('Revocatoria solicitada');
    expect(revocationRequestLabel('en_revision')).toBe('Revocatoria en revisión');
    expect(REVOCATION_REQUEST_LABELS.solicitada).toBe('Revocatoria solicitada');
  });

  it('no devuelve label/tone cuando no hay solicitud activa', () => {
    expect(revocationRequestLabel(null)).toBeNull();
    expect(revocationRequestLabel('aprobada')).toBeNull();
    expect(revocationRequestTone(null)).toBeNull();
  });

  it('mapea tone semántico por sub-estado', () => {
    expect(revocationRequestTone('solicitada')).toBe('warning');
    expect(revocationRequestTone('en_revision')).toBe('info');
  });
});
