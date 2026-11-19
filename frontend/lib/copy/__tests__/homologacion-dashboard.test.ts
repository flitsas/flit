// HU #12700 (Feature #12692, épica #12551) — palabras de estado en dashboard (A18, A19, E03).
// Rediseño del Inicio en 3 dashboards: «Total trámites», «Esperan mi decisión» y «Entregados hoy» ya no son KPIs del
// Inicio; se conserva lo que sigue vigente (héroes sin unificar y el casing de E03).
import { describe, expect, it } from 'vitest';
import { DASHBOARD_HERO_PREFIX } from '@/components/atom/modules/GestorDashboard';
import { OT_DASHBOARD_HERO_TITLE, OT_KPI_PENDIENTES_HOY } from '@/components/atom/modules/OtDashboard';
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

  it('AC2 — A19 N/A: los KPIs de la cola OT no se unifican con el catálogo', () => {
    expect(isNaCopyKey('A19')).toBe(true);
    expect(copyLabel('A19')).toBeUndefined();
    expect(OT_KPI_PENDIENTES_HOY).not.toBe(COPY.E03);
  });

  it('AC3 — E03 conserva el casing «Total trámites»', () => {
    expect(COPY.E03).toBe('Total trámites');
  });
});
