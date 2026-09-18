// HU #12694 (Feature #12689, épica #12551) — lista QA de campos cuyo texto cambió.
// Uso de ejemplo
// qaChangedFields() → [{ id: 'A01', superficie: 'Listado OT', textoAnterior: 'Propietario / vendedor', textoGanador: 'Vendedor' }, …]
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { COPY_NA_KEYS } from '../copy-catalog';
import {
  QA_EXCLUDED_GLOSSARY_IDS,
  QA_LIST_REPO_PATH,
  qaChangedFieldIds,
  qaChangedFields,
  type QaChangedField,
} from '../qa-campos-cambiados';

const REPO_ROOT = join(dirname(fileURLToPath(import.meta.url)), '../../../../');

function hasRequiredColumns(row: QaChangedField): boolean {
  return (
    row.id.trim().length > 0 &&
    row.superficie.trim().length > 0 &&
    row.textoAnterior.trim().length > 0 &&
    row.textoGanador.trim().length > 0
  );
}

describe('lista QA de campos cambiados (HU #12694)', () => {
  it('AC1 — cada fila tiene ID de glosario, superficie, texto anterior y texto ganador', () => {
    const rows = qaChangedFields();
    expect(rows.length).toBeGreaterThan(0);

    for (const row of rows) {
      expect(hasRequiredColumns(row)).toBe(true);
      expect(row.textoAnterior).not.toBe(row.textoGanador);
      expect(row.textoGanador).not.toBe('N/A');
    }

    const a01 = rows.find((row) => row.id === 'A01');
    expect(a01).toMatchObject({
      superficie: 'Listado OT',
      textoAnterior: 'Propietario / vendedor',
      textoGanador: 'Vendedor',
    });
  });

  it('AC2 — filas RN-07 o Ganador igual al actual no aparecen como cambio a certificar', () => {
    const ids = qaChangedFieldIds();
    const excluded = new Set<string>(QA_EXCLUDED_GLOSSARY_IDS);

    for (const id of COPY_NA_KEYS) {
      expect(ids).not.toContain(id);
    }

    for (const id of excluded) {
      expect(ids).not.toContain(id);
    }

    expect(ids).not.toContain('A02');
    expect(ids).not.toContain('A11');
    expect(ids).not.toContain('A22');
    expect(ids).not.toContain('B07');
    expect(ids).not.toContain('C01');
    expect(ids).not.toContain('D04');
    expect(ids).not.toContain('D07');
  });

  it('AC3 — la lista vive en el repo junto al glosario y se puede citar en Discussion', () => {
    expect(QA_LIST_REPO_PATH).toBe('docs/ado-drafts/epic-12551/lista-qa-campos-cambiados.md');

    const listPath = join(REPO_ROOT, QA_LIST_REPO_PATH);
    const glossaryPath = join(REPO_ROOT, 'docs/ado-drafts/epic-12551/inventario-glosario.md');

    expect(existsSync(listPath)).toBe(true);
    expect(existsSync(glossaryPath)).toBe(true);
    expect(dirname(listPath)).toBe(dirname(glossaryPath));

    const markdown = readFileSync(listPath, 'utf8');
    expect(markdown).toContain('# Lista QA de campos cambiados');
    expect(markdown).toContain('| ID | Superficie | Texto anterior | Texto ganador |');

    for (const row of qaChangedFields()) {
      expect(markdown).toContain(row.id);
      expect(markdown).toContain(row.superficie);
      expect(markdown).toContain(row.textoAnterior);
      expect(markdown).toContain(row.textoGanador);
    }

    expect(markdown).toContain(QA_LIST_REPO_PATH);
  });
});
