// HU #12700 (Feature #12692, épica #12551) — palabras de estado en dashboard (A18, A19, E03).
import { describe, expect, it } from 'vitest';
import {
  DASHBOARD_HERO_PREFIX,
  DASHBOARD_KPI_TOTAL_LABEL,
} from '@/components/atom/modules/Dashboard';
import {
  OT_DASHBOARD_HERO_TITLE,
  OT_KPI_ENTREGADOS_HOY,
  OT_KPI_ESPERAN_MI_DECISION,
} from '@/components/atom/modules/OtDashboard';
import { COPY, copyLabel, isNaCopyKey } from '../copy-catalog';
import { estadoLabel } from '@/lib/tramites/estados';

describe('homologación dashboard (HU #12700)', () => {
  it('AC1 — A18 N/A no unifica héroes; KPI de estado de negocio usa catálogo B', () => {
    expect(isNaCopyKey('A18')).toBe(true);
    expect(copyLabel('A18')).toBeUndefined();
    expect(DASHBOARD_HERO_PREFIX).toBe('Hola,');
    expect(OT_DASHBOARD_HERO_TITLE).toBe('Tu cola de trabajo');
    expect(OT_DASHBOARD_HERO_TITLE.startsWith(DASHBOARD_HERO_PREFIX)).toBe(false);
    expect(estadoLabel('entregado')).toBe('Entregado');
    expect(estadoLabel('aprobado')).toBe('Aprobado');
  });

  it('AC2 — A19 N/A no clona KPI Total trámites ↔ Esperan mi decisión ni unifica Entregados hoy', () => {
    expect(isNaCopyKey('A19')).toBe(true);
    expect(copyLabel('A19')).toBeUndefined();
    expect(DASHBOARD_KPI_TOTAL_LABEL).toBe(COPY.E03);
    expect(OT_KPI_ESPERAN_MI_DECISION).toBe('Esperan mi decisión');
    expect(OT_KPI_ESPERAN_MI_DECISION).not.toBe(COPY.E03);
    expect(OT_KPI_ENTREGADOS_HOY).toBe('Entregados hoy');
    expect(OT_KPI_ENTREGADOS_HOY).not.toBe(estadoLabel('entregado'));
  });

  it('AC3 — E03 unifica casing Total trámites en dashboard y reportes gestor', () => {
    expect(COPY.E03).toBe('Total trámites');
    expect(DASHBOARD_KPI_TOTAL_LABEL).toBe(COPY.E03);
    expect(DASHBOARD_KPI_TOTAL_LABEL).not.toBe('Total Trámites');
  });
});
