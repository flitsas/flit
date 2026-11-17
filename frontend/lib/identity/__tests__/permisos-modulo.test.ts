/**
 * D1 — solo quien GESTIONA el módulo de identidad consulta a Verify desde el detalle de persona.
 * Misma regla que `RequireIdentityModuleManage` (IdentityModuleAccessFilter): SuperAdmin, o
 * `validaciones.manage` en un usuario que no sea de organismo de tránsito.
 *
 * Uso de ejemplo: puedeGestionarIdentidad(decodeJwtPayload(getToken())) → boolean
 */
import { describe, expect, it } from 'vitest';

import { puedeGestionarIdentidad } from '@/lib/identity/permisos-modulo';

describe('puedeGestionarIdentidad', () => {
  it('con validaciones.manage → true (sin distinguir mayúsculas)', () => {
    expect(puedeGestionarIdentidad({ permissions: ['VALIDACIONES.MANAGE'] })).toBe(true);
  });

  it('solo validaciones.read → false', () => {
    expect(puedeGestionarIdentidad({ permissions: ['validaciones.read'] })).toBe(false);
  });

  it('SuperAdmin → true aunque no traiga el permiso', () => {
    expect(puedeGestionarIdentidad({ role_code: 'SuperAdmin', permissions: [] })).toBe(true);
  });

  it('organismo de tránsito → false aunque tenga el permiso', () => {
    expect(
      puedeGestionarIdentidad({ entity_type: 'TRANSIT_OFFICE', permissions: ['validaciones.manage'] }),
    ).toBe(false);
  });

  it('sin token → false', () => {
    expect(puedeGestionarIdentidad(null)).toBe(false);
  });
});
