import { describe, expect, it } from 'vitest';
import {
  BATCH_MAX_FILES,
  BATCH_MAX_FILE_BYTES,
  buildReviewItems,
  tiposDelLote,
  validateBatch,
} from '@/hooks/useProcedureBatchUpload';
import type {
  BatchOcrPiece,
  ChecklistItemView,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

// ── Ayudas ───────────────────────────────────────────────────────────────────

function pieza(over: Partial<BatchOcrPiece> = {}): BatchOcrPiece {
  return {
    tipo: 'soat',
    sourceFilename: 'expediente.pdf',
    filename: 'soat_expediente.pdf',
    mimetype: 'application/pdf',
    sizeBytes: 1024,
    paginas: [3],
    totalPaginasOrigen: 16,
    confianza: 0.9,
    motivo: 'Poliza SOAT',
    data: { es_valido: true },
    analisisError: null,
    contentBase64: 'JVBERi0=',
    ...over,
  };
}

function item(key: string, docTipo?: string): ChecklistItemView {
  return {
    key,
    label: key,
    obligatorio: true,
    satisfied: false,
    docTipo: docTipo ?? key,
  } as ChecklistItemView;
}

function adjunto(tipo: string): ProcedureAttachment {
  return {
    id: `att-${tipo}`,
    tipo,
    filename: `${tipo}.pdf`,
    mimetype: 'application/pdf',
    sizeBytes: 100,
  } as ProcedureAttachment;
}

function file(name: string, size: number): File {
  const f = new File(['x'], name);
  Object.defineProperty(f, 'size', { value: size });
  return f;
}

// ── Qué tipos se mandan a clasificar ─────────────────────────────────────────

describe('tiposDelLote', () => {
  // HU #12034 — los tipos con OCR los declara el backend y se inyectan; ya no dependen de la
  // modalidad del trámite. El mismo documento sirve en matrícula y en traspaso sin tocar código.
  const CON_OCR = new Set(['factura', 'aduana', 'impronta', 'soat', 'rtm']);

  it('solo propone tipos que el checklist realmente muestra', () => {
    // El checklist es configurable por tenant: proponer un tipo oculto sería ofrecer un campo
    // que el operador no tiene dónde llenar.
    const items = [item('factura'), item('soat'), item('cedulas')];

    expect(tiposDelLote(CON_OCR, items)).toEqual(['factura', 'soat']);
  });

  it('no depende de la modalidad: el mismo tipo sirve en cualquier trámite', () => {
    const items = [item('factura'), item('aduana'), item('impronta'), item('soat'), item('rtm')];

    // Antes, `factura` y `aduana` se caían en traspaso por estar fuera de la lista de esa
    // modalidad. Ahora manda el catálogo: si el trámite lo pide y tiene prompt, se analiza.
    expect(tiposDelLote(CON_OCR, items)).toEqual([
      'factura',
      'aduana',
      'impronta',
      'soat',
      'rtm',
    ]);
  });

  it('descarta los tipos que no tienen OCR', () => {
    expect(tiposDelLote(CON_OCR, [item('factura'), item('cedulas')])).toEqual(['factura']);
  });

  it('resuelve el tipo por docTipo cuando difiere de la clave', () => {
    expect(tiposDelLote(CON_OCR, [item('doc_soat_vigente', 'soat')])).toEqual(['soat']);
  });

  it('sin ítems compatibles devuelve vacío', () => {
    expect(tiposDelLote(CON_OCR, [item('cedulas')])).toEqual([]);
  });

  it('si no se pudo consultar el backend propone todos los visibles, no ninguno', () => {
    // Falla ABIERTO a propósito: el backend descarta los que no tengan prompt. Repartir de menos
    // en silencio es justo el defecto que esta HU cierra.
    expect(tiposDelLote(null, [item('factura'), item('cedulas')])).toEqual(['factura', 'cedulas']);
  });
});

// ── Topes ────────────────────────────────────────────────────────────────────

describe('validateBatch', () => {
  it('acepta un lote normal', () => {
    expect(validateBatch([file('a.pdf', 1000), file('b.pdf', 2000)])).toBeNull();
  });

  it('rechaza un lote vacío', () => {
    expect(validateBatch([])).toContain('al menos un archivo');
  });

  it('rechaza más archivos del tope', () => {
    const files = Array.from({ length: BATCH_MAX_FILES + 1 }, (_, i) => file(`f${i}.pdf`, 10));

    expect(validateBatch(files)).toContain(String(BATCH_MAX_FILES));
  });

  it('nombra el archivo que se pasa de tamaño', () => {
    const error = validateBatch([file('ok.pdf', 10), file('enorme.pdf', BATCH_MAX_FILE_BYTES + 1)]);

    expect(error).toContain('enorme.pdf');
  });

  it('rechaza cuando el peso total se pasa', () => {
    const files = Array.from({ length: 5 }, (_, i) => file(`f${i}.pdf`, 30 * 1024 * 1024));

    expect(validateBatch(files)).toContain('en total');
  });
});

// ── La revisión: qué llega marcado y qué no ──────────────────────────────────

describe('buildReviewItems', () => {
  it('marca una pieza válida que llena un campo vacío', () => {
    const [r] = buildReviewItems([pieza()], [], null);

    expect(r.decision).toBe('accept');
    expect(r.conflicto).toBeNull();
    expect(r.duplicado).toBe(false);
    expect(r.evaluation.rechazado).toBe(false);
  });

  it('nunca pisa un adjunto existente: marca conflicto y llega desmarcada', () => {
    const [r] = buildReviewItems([pieza()], [adjunto('soat')], null);

    expect(r.decision).toBe('skip');
    expect(r.conflicto?.id).toBe('att-soat');
  });

  it('con dos piezas del mismo tipo gana la de mayor confianza', () => {
    const items = buildReviewItems(
      [
        pieza({ confianza: 0.6, paginas: [2] }),
        pieza({ confianza: 0.95, paginas: [7] }),
      ],
      [],
      null,
    );

    expect(items[0].decision).toBe('skip');
    expect(items[0].duplicado).toBe(true);
    expect(items[1].decision).toBe('accept');
    expect(items[1].duplicado).toBe(false);
  });

  it('con confianza empatada sólo una queda marcada', () => {
    const items = buildReviewItems(
      [pieza({ paginas: [2] }), pieza({ paginas: [7] })],
      [],
      null,
    );

    expect(items.filter((i) => i.decision === 'accept')).toHaveLength(1);
  });

  it('una pieza cuyo VIN no cruza llega desmarcada y con el motivo', () => {
    const [r] = buildReviewItems(
      [pieza({ data: { es_valido: true, vehiculo_vin: 'AAAAAAAAAAAAAAAAA' } })],
      [],
      'BBBBBBBBBBBBBBBBB',
    );

    expect(r.decision).toBe('skip');
    expect(r.evaluation.rechazado).toBe(true);
    expect(r.evaluation.motivo).toContain('VIN');
  });

  it('una pieza que no pasa la validación de tipo llega desmarcada', () => {
    const [r] = buildReviewItems([pieza({ data: { es_valido: false } })], [], null);

    expect(r.decision).toBe('skip');
    expect(r.evaluation.rechazado).toBe(true);
  });

  it('si el análisis falló se conserva la pieza con su motivo, desmarcada', () => {
    const [r] = buildReviewItems(
      [pieza({ data: null, analisisError: 'Lectura automática no disponible.' })],
      [],
      null,
    );

    expect(r.decision).toBe('skip');
    expect(r.evaluation.motivo).toBe('Lectura automática no disponible.');
    // El binario sigue ahí: el operador puede confirmarla igual.
    expect(r.piece.contentBase64).not.toBe('');
  });

  it('los identificadores distinguen piezas del mismo tipo y archivo', () => {
    const items = buildReviewItems(
      [pieza({ paginas: [2] }), pieza({ paginas: [7] })],
      [],
      null,
    );

    expect(items[0].id).not.toBe(items[1].id);
  });

  it('un conflicto en un tipo no arrastra a los demás', () => {
    const items = buildReviewItems(
      [pieza({ tipo: 'soat' }), pieza({ tipo: 'rtm', filename: 'rtm_expediente.pdf' })],
      [adjunto('soat')],
      null,
    );

    expect(items[0].decision).toBe('skip');
    expect(items[1].decision).toBe('accept');
  });
});
