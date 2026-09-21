// HU #12701 (Feature #12692, épica #12551) — títulos de Reportes y Usuarios (A20–A23).
import { describe, expect, it } from 'vitest';
import { GESTOR_REPORTES_TAB_DEFS } from '@/components/atom/modules/Reportes';
import { OT_REPORTES_TABS } from '@/components/admin/transit-offices/OtReportsConsole';
import { COPY, copyForOtAndGestor, copyLabel, isNaCopyKey } from '../copy-catalog';

describe('homologación Reportes y Usuarios (HU #12701)', () => {
  it('AC1 — A22 Ahora mismo es compartido; Consultas personalizadas ya coincidía', () => {
    expect(COPY.A22).toBe('Ahora mismo');
    expect(copyForOtAndGestor('A22')).toEqual({ ot: COPY.A22, gestor: COPY.A22 });
    expect(OT_REPORTES_TABS.find((t) => t.id === 'ahora')?.label).toBe(COPY.A22);

    const gestorConsultas = GESTOR_REPORTES_TAB_DEFS.find((t) => t.id === 'consultas')?.label;
    const otConsultas = OT_REPORTES_TABS.find((t) => t.id === 'consultas')?.label;
    expect(gestorConsultas).toBe('Consultas personalizadas');
    expect(otConsultas).toBe(gestorConsultas);
  });

  it('AC2 — A20/A21 N/A no clonan pestañas solo-rol (Uso, Análisis, Revisores)', () => {
    expect(isNaCopyKey('A20')).toBe(true);
    expect(isNaCopyKey('A21')).toBe(true);
    expect(copyLabel('A20')).toBeUndefined();
    expect(copyLabel('A21')).toBeUndefined();

    const gestorLabels = GESTOR_REPORTES_TAB_DEFS.map((t) => t.label);
    const otLabels = OT_REPORTES_TABS.map((t) => t.label);

    expect(gestorLabels).toContain('Uso del aplicativo');
    expect(otLabels).not.toContain('Uso del aplicativo');
    expect(otLabels).toContain('Análisis');
    expect(gestorLabels).not.toContain('Análisis');
    expect(otLabels).toContain('Revisores');
    expect(gestorLabels).not.toContain('Revisores');
  });

  it('AC3 — H1 Usuarios OT y gestor leen COPY.A23', () => {
    expect(COPY.A23).toBe('Usuarios');
    expect(COPY.A23).not.toBe('Administración OT — Usuarios');
    expect(copyForOtAndGestor('A23')).toEqual({ ot: COPY.A23, gestor: COPY.A23 });
  });
});
