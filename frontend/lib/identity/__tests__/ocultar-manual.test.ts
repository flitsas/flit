import { describe, expect, it } from 'vitest';
import { ocultarManualAlCliente } from '@/lib/identity/ocultar-manual';

const manual = (over: Record<string, unknown> = {}) => ({
  id: 'v1',
  provider: 'manual',
  status: 'manual_activo',
  approvalOrigin: null,
  rejectionReasonCode: null,
  ...over,
});

describe('lo manual solo lo ve el Super Admin', () => {
  it('el Super Admin recibe los datos intactos', () => {
    const d = manual();
    expect(ocultarManualAlCliente(d, true)).toBe(d);
  });

  it('para el cliente una validación manual es una biométrica normal (proveedor y estados)', () => {
    expect(ocultarManualAlCliente(manual(), false)).toMatchObject({ provider: 'kyverum', status: 'enviado' });
    expect(ocultarManualAlCliente(manual({ status: 'pendiente_revision_manual' }), false)).toMatchObject({
      provider: 'kyverum',
      status: 'en_proceso',
    });
  });

  it('aprobada manual se muestra como aprobada normal y el motivo del Super Admin no sale', () => {
    const a = ocultarManualAlCliente(manual({ status: 'aprobado', approvalOrigin: 'manual' }), false);
    expect(a).toMatchObject({ provider: 'kyverum', status: 'aprobado', approvalOrigin: 'automatica' });
    const r = ocultarManualAlCliente(manual({ status: 'rechazado', rejectionReasonCode: 'imagen_borrosa' }), false);
    expect(r).toMatchObject({ status: 'rechazado', rejectionReasonCode: null });
  });

  it('recorre listas y objetos anidados sin tocar lo demás', () => {
    const out = ocultarManualAlCliente(
      { validations: [manual(), { id: 'v2', provider: 'kyverum', status: 'aprobado', score: 91 }], total: 2 },
      false,
    );
    expect(out.validations[0]).toMatchObject({ provider: 'kyverum', status: 'enviado' });
    expect(out.validations[1]).toEqual({ id: 'v2', provider: 'kyverum', status: 'aprobado', score: 91 });
    expect(out.total).toBe(2);
    expect(JSON.stringify(out)).not.toMatch(/manual/i);
  });

  it('valores no objeto pasan igual', () => {
    expect(ocultarManualAlCliente(null, false)).toBeNull();
    expect(ocultarManualAlCliente('x', false)).toBe('x');
  });
});
