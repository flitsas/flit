// Soporte del impuesto departamental en `asignado` (modal «Enviar al OT»): sube el adjunto
// `liquidacion_impuesto` y devuelve el texto para la UI; nunca lanza.
import { describe, expect, it, vi } from 'vitest';
import {
  cargarSoporteImpuestoAsignado,
  COPY_SOPORTE_IMPUESTO_ERROR,
  COPY_SOPORTE_IMPUESTO_REGISTRADO,
  TIPO_SOPORTE_IMPUESTO,
} from '../soporte-impuesto-asignado';
import { COPY_ADJUNTO_BLOQUEADO_FLITO } from '../flito';

const pdf = () => new File(['%PDF-1.4'], 'impuesto.pdf', { type: 'application/pdf' });

describe('cargarSoporteImpuestoAsignado', () => {
  it('sube el PDF como liquidacion_impuesto y lo da por registrado', async () => {
    const uploadAttachment = vi.fn(async () => ({ id: 'att-1' }));
    const file = pdf();

    const r = await cargarSoporteImpuestoAsignado({ uploadAttachment }, 'inst-1', file, 'tenant-1');

    expect(TIPO_SOPORTE_IMPUESTO).toBe('liquidacion_impuesto');
    expect(uploadAttachment).toHaveBeenCalledWith('inst-1', 'liquidacion_impuesto', file, 'tenant-1');
    expect(r).toEqual({ estado: 'registrado', mensaje: COPY_SOPORTE_IMPUESTO_REGISTRADO });
  });

  it('si FLITO ya cargó el comprobante muestra que no se puede reemplazar', async () => {
    const err = Object.assign(new Error('conflict'), {
      status: 409,
      problem: { error: 'adjunto_bloqueado_flito', status: 409 },
    });
    const uploadAttachment = vi.fn(async () => {
      throw err;
    });

    const r = await cargarSoporteImpuestoAsignado({ uploadAttachment }, 'inst-1', pdf());

    expect(r).toEqual({ estado: 'error', mensaje: COPY_ADJUNTO_BLOQUEADO_FLITO });
  });

  it('otro error usa el mensaje del API o el de respaldo, sin lanzar', async () => {
    const conMensaje = vi.fn(async () => {
      throw new Error('El trámite no admite cambios en este estado.');
    });
    const sinMensaje = vi.fn(async () => {
      throw { status: 500 };
    });

    const a = await cargarSoporteImpuestoAsignado({ uploadAttachment: conMensaje }, 'inst-1', pdf());
    const b = await cargarSoporteImpuestoAsignado({ uploadAttachment: sinMensaje }, 'inst-1', pdf());

    expect(a).toEqual({ estado: 'error', mensaje: 'El trámite no admite cambios en este estado.' });
    expect(b).toEqual({ estado: 'error', mensaje: COPY_SOPORTE_IMPUESTO_ERROR });
  });
});
