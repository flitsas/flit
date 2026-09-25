// Uso de ejemplo: checklist docs/auditoria-ui-admin-e4-12732.md + OtHubLayout surface plano — HU #12732.
import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const repoRoot = join(__dirname, '../..');
const auditDoc = readFileSync(
  join(repoRoot, 'docs/auditoria-ui-admin-e4-12732.md'),
  'utf8',
);
const otHub = readFileSync(
  join(repoRoot, 'frontend/components/admin/transit-offices/OtHubLayout.tsx'),
  'utf8',
);

describe('HU #12732 — AC1 inventario de auditoría', () => {
  it('happy: documento de auditoría tiene fila por módulo con veredicto', () => {
    expect(auditDoc).toMatch(/OT hub — layout default/);
    expect(auditDoc).toMatch(/OT — Motor de reglas/);
    expect(auditDoc).toMatch(/OT — Mandatos/);
    expect(auditDoc).toMatch(/\|\s*\*\*Diferido\*\*\s*\|/);
    expect(auditDoc).toMatch(/Corregido #12731|OK previo/);
  });
});

describe('HU #12732 — AC2/AC3 superficie y tablas (vía #12731)', () => {
  it('contrato: OtHubLayout default surface plano', () => {
    expect(otHub).toContain('surface = "plano"');
  });
});

describe('HU #12732 — AC4/AC5/AC6 documentación de alcance', () => {
  it('edge: deuda diferida documentada (admin compañías / plataforma)', () => {
    expect(auditDoc).toMatch(/Admin compañías|Diferido/);
    expect(auditDoc).toMatch(/D8/);
  });
});
