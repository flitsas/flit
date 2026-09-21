// HU #12695 (Feature #12690) — cabeceras de listado OT y gestor (A01–A06).
// Uso de ejemplo
// COPY.A01 → 'Vendedor' en OT_PROCEDURES_COLUMNS y TRAMITES_COLUMNS
import { describe, expect, it } from 'vitest';
import { COPY } from '../copy-catalog';
import { OT_PROCEDURES_COLUMNS } from '@/lib/admin/ot-procedures-columns';
import { otProceduresExportFields } from '@/lib/admin/ot-procedures-export';
import { TRAMITES_COLUMNS, tramitesExportFields } from '@/lib/tramites/tramites-table-columns';

function labelOf(columns: readonly { key: string; label: string }[], key: string): string {
  const found = columns.find((c) => c.key === key);
  if (!found) throw new Error(`missing column ${key}`);
  return found.label;
}

describe('cabeceras de listado homologadas (HU #12695)', () => {
  it('AC1 — el mismo dato usa el mismo vocablo; A02 conserva layout distinto', () => {
    expect(labelOf(TRAMITES_COLUMNS, 'propietario')).toBe(COPY.A01);
    expect(labelOf(OT_PROCEDURES_COLUMNS, 'vendedor')).toBe(COPY.A01);

    expect(labelOf(TRAMITES_COLUMNS, 'tramite')).toBe(COPY.A03);
    expect(labelOf(OT_PROCEDURES_COLUMNS, 'tipoTramite')).toBe(COPY.A03);

    expect(labelOf(TRAMITES_COLUMNS, 'fechaCreacion')).toBe(COPY.A04);
    expect(labelOf(OT_PROCEDURES_COLUMNS, 'fechaRadicacion')).toBe(COPY.A04);

    expect(labelOf(TRAMITES_COLUMNS, 'secretaria')).toBe(COPY.A05);

    expect(labelOf(TRAMITES_COLUMNS, 'gestor')).toBe(COPY.A06);
    expect(labelOf(OT_PROCEDURES_COLUMNS, 'empresaGestor')).toBe(COPY.A06);

    expect(labelOf(TRAMITES_COLUMNS, 'placa')).toBe('Vehículo');
    expect(labelOf(OT_PROCEDURES_COLUMNS, 'vin')).toBe(COPY.A02Vin);
    expect(labelOf(OT_PROCEDURES_COLUMNS, 'placa')).toBe(COPY.A02Placa);
    expect(OT_PROCEDURES_COLUMNS.some((c) => c.label === 'Vehículo')).toBe(false);
  });

  it('AC2 — columnas solo-gestor no se clonan al OT', () => {
    const otKeys = OT_PROCEDURES_COLUMNS.map((c) => c.key);
    expect(otKeys).not.toContain('marcas');
    expect(otKeys).not.toContain('fuente');
    expect(otKeys).not.toContain('paso');
    expect(otKeys).not.toContain('confirmadoRunt');

    const gestorKeys = TRAMITES_COLUMNS.map((c) => c.key);
    expect(gestorKeys).toContain('marcas');
    expect(gestorKeys).toContain('fuente');
    expect(gestorKeys).toContain('paso');
  });

  it('AC3 — cabeceras Excel coinciden con las de pantalla para el mismo dato', () => {
    const gestorExport = tramitesExportFields(['propietario', 'tramite', 'secretaria', 'fechaCreacion']);
    expect(gestorExport.find((f) => f.id === 'vendedor')?.label).toBe(COPY.A01);
    expect(gestorExport.find((f) => f.id === 'tramite')?.label).toBe(COPY.A03);
    expect(gestorExport.find((f) => f.id === 'secretaria')?.label).toBe(COPY.A05);
    expect(gestorExport.find((f) => f.id === 'fechaCreacion')?.label).toBe(COPY.A04);

    const otExport = otProceduresExportFields([
      'vendedor',
      'tipoTramite',
      'empresaGestor',
      'fechaRadicacion',
    ]);
    expect(otExport.find((f) => f.id === 'vendedor')?.label).toBe(COPY.A01);
    expect(otExport.find((f) => f.id === 'tipoTramite')?.label).toBe(COPY.A03);
    expect(otExport.find((f) => f.id === 'gestor')?.label).toBe(COPY.A06);
    expect(otExport.find((f) => f.id === 'fechaRadicacion')?.label).toBe(COPY.A04);
  });
});
