import { describe, expect, it } from 'vitest';
import { textoRevision } from '../ManualReviewDetailModal';
import type { ManualDetail } from '@/lib/api/types/manual-review';

const GUID = '019f975a-055e-796f-b2b3-b0a0540e22bb';
const base = {
  reviewedAt: '2026-11-19T21:08:00Z',
} as unknown as ManualDetail;

describe('textoRevision — nunca muestra el id del revisor', () => {
  it('con revisor completo muestra nombre, correo y rol', () => {
    const t = textoRevision('Aprobada', {
      ...base,
      reviewedBy: 'Usuario Demo',
      reviewer: { name: 'Usuario Demo', email: 'demo@flit.local', role: 'Super Administrador' },
    });
    expect(t).toContain('Aprobada por Usuario Demo (demo@flit.local, Super Administrador)');
  });

  it('si el nombre no se resuelve y reviewedBy es un guid, no aparece el guid', () => {
    const t = textoRevision('Aprobada', { ...base, reviewedBy: GUID, reviewer: null });
    expect(t).not.toContain(GUID);
    expect(t).toMatch(/^Aprobada el /);
  });

  it('sin ningún dato de revisor devuelve solo la fecha sin "null"', () => {
    const t = textoRevision('Rechazada', { ...base, reviewedBy: null, reviewer: null });
    expect(t).not.toContain('null');
    expect(t).not.toContain('por');
  });
});
