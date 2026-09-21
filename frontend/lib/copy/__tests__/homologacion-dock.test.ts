// HU #12699 (Feature #12692, épica #12551) — Dock e Identidad (A17, B21).
import { describe, expect, it } from 'vitest';
import { IDENTIDAD_MODULE_TITLE } from '@/components/atom/modules/Validaciones';
import { DOCK_GROUP_LABEL, SPA_DOCK_ITEM_LABEL } from '@/components/atom/dock/dockGroups';
import { OT_HUB_TABS } from '@/components/admin/transit-offices/ot-nav';
import { resolveNavigableModuleIds } from '@/lib/nav/modules';
import { COPY, copyForOtAndGestor, isNaCopyKey } from '../copy-catalog';

describe('homologación dock e Identidad (HU #12699)', () => {
  it('AC1 — píldora del dock y H1 del módulo usan COPY.A17 (Identidad)', () => {
    expect(COPY.A17).toBe('Identidad');
    expect(IDENTIDAD_MODULE_TITLE).toBe(COPY.A17);
    expect(SPA_DOCK_ITEM_LABEL.validaciones).toBe(COPY.A17);
    expect(DOCK_GROUP_LABEL.identidad).toBe(COPY.A17);
    expect(copyForOtAndGestor('A17')).toEqual({ ot: COPY.A17, gestor: COPY.A17 });
  });

  it('AC2 — Admin OT sin validaciones.read no ve Identidad (RN-07, no es conflicto)', () => {
    const withoutRead = resolveNavigableModuleIds({
      accessibleCodes: ['tramites', 'reportes', 'usuarios'],
      isSuperAdmin: false,
      isOtAdmin: true,
      canReadLogQx: false,
      canReadIctLogs: false,
    });
    expect(withoutRead).not.toContain('validaciones');

    const withRead = resolveNavigableModuleIds({
      accessibleCodes: ['tramites', 'reportes', 'usuarios', 'validaciones'],
      isSuperAdmin: false,
      isOtAdmin: true,
      canReadLogQx: false,
      canReadIctLogs: false,
    });
    expect(withRead).toContain('validaciones');
  });

  it('AC3 — Trámites, Reportes, Usuarios y Ayuda son el mismo label B21 en OT y gestor', () => {
    expect(isNaCopyKey('B21Tramites')).toBe(false);
    expect(SPA_DOCK_ITEM_LABEL.tramites).toBe(COPY.B21Tramites);
    expect(SPA_DOCK_ITEM_LABEL.reportes).toBe(COPY.B21Reportes);
    expect(SPA_DOCK_ITEM_LABEL.usuarios).toBe(COPY.B21Usuarios);
    expect(SPA_DOCK_ITEM_LABEL.ayuda).toBe(COPY.B21Ayuda);

    expect(DOCK_GROUP_LABEL.tramites).toBe(COPY.B21Tramites);
    expect(DOCK_GROUP_LABEL.reportes).toBe(COPY.B21Reportes);
    expect(DOCK_GROUP_LABEL.usuarios).toBe(COPY.B21Usuarios);

    const otTramites = OT_HUB_TABS.find((t) => t.id === 'client-procedures')?.label;
    const otUsuarios = OT_HUB_TABS.find((t) => t.id === 'usuarios')?.label;
    const otReportes = OT_HUB_TABS.find((t) => t.id === 'reportes')?.label;
    expect(otTramites).toBe(COPY.B21Tramites);
    expect(otUsuarios).toBe(COPY.B21Usuarios);
    expect(otReportes).toBe(COPY.B21Reportes);

    expect(copyForOtAndGestor('B21Tramites')).toEqual({
      ot: COPY.B21Tramites,
      gestor: COPY.B21Tramites,
    });
    expect(copyForOtAndGestor('B21Reportes')).toEqual({
      ot: COPY.B21Reportes,
      gestor: COPY.B21Reportes,
    });
    expect(copyForOtAndGestor('B21Usuarios')).toEqual({
      ot: COPY.B21Usuarios,
      gestor: COPY.B21Usuarios,
    });
    expect(copyForOtAndGestor('B21Ayuda')).toEqual({
      ot: COPY.B21Ayuda,
      gestor: COPY.B21Ayuda,
    });
  });
});
