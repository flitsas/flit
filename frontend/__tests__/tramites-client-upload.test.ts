import { afterEach, describe, expect, it, vi } from 'vitest';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  base64ToPdfFile,
  evaluateOcr,
  esTipoOcr,
  normalizeVin,
  resumirVins,
  vinsDelDocumento,
} from '@/hooks/useProcedureDocuments';

/**
 * uploadAttachment: subida directa navegador→S3 (presigned). Verifica el flujo de 3 pasos
 * (presign → POST a S3 → register), el orden de los campos firmados antes del 'file', y que el
 * sha256 se calcula en el navegador y viaja en el register (el binario NO pasa por el API).
 */
describe('tramitesClient.uploadAttachment (presigned)', () => {
  afterEach(() => vi.restoreAllMocks());

  it('hace presign, sube a S3 y registra la metadata con el sha256 del cliente', async () => {
    const calls: Array<{ url: string; init?: RequestInit }> = [];
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      calls.push({ url: String(url), init });
      if (String(url).includes('/attachments/presign')) {
        return new Response(
          JSON.stringify({
            storagePath: 'file_xyz',
            url: 'https://s3.test/upload',
            fields: { key: 'tramites/k/obj', policy: 'pol' },
          }),
          { status: 200, headers: { 'content-type': 'application/json' } },
        );
      }
      if (String(url) === 'https://s3.test/upload') {
        return new Response(null, { status: 204 }); // S3 POST policy OK
      }
      if (String(url).includes('/attachments/register')) {
        return new Response(
          JSON.stringify({
            id: 'att-1',
            tipo: 'factura',
            filename: 'doc.pdf',
            mimetype: 'application/pdf',
            sizeBytes: 5,
            sha256: 'x',
            source: 'user',
            uploadedAt: '2026-06-26T00:00:00Z',
          }),
          { status: 201, headers: { 'content-type': 'application/json' } },
        );
      }
      throw new Error(`unexpected fetch: ${url}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    const file = new File([new Uint8Array([1, 2, 3, 4, 5])], 'doc.pdf', { type: 'application/pdf' });
    const result = await tramitesClient.uploadAttachment('inst-1', 'factura', file, 'tenant-1');

    expect(result.id).toBe('att-1');
    expect(fetchMock).toHaveBeenCalledTimes(3);

    // 1) presign con la metadata del archivo
    const presign = calls.find((c) => c.url.includes('/attachments/presign'))!;
    expect(JSON.parse(presign.init!.body as string)).toMatchObject({
      tipo: 'factura',
      filename: 'doc.pdf',
      mimetype: 'application/pdf',
      sizeBytes: 5,
    });

    // 2) POST a S3: los campos firmados van ANTES del 'file'
    const s3 = calls.find((c) => c.url === 'https://s3.test/upload')!;
    const form = s3.init!.body as FormData;
    const keys = [...form.keys()];
    expect(keys).toEqual(['key', 'policy', 'file']);
    expect(keys.indexOf('file')).toBe(keys.length - 1);

    // 3) register con storagePath y sha256 (64 hex calculado en el navegador)
    const register = calls.find((c) => c.url.includes('/attachments/register'))!;
    const body = JSON.parse(register.init!.body as string);
    expect(body.storagePath).toBe('file_xyz');
    expect(body.sha256).toMatch(/^[0-9a-f]{64}$/);
  });

  it('lanza si la subida a S3 falla (no llega a register)', async () => {
    const fetchMock = vi.fn(async (url: string) => {
      if (String(url).includes('/attachments/presign')) {
        return new Response(
          JSON.stringify({ storagePath: 'f', url: 'https://s3.test/upload', fields: {} }),
          { status: 200, headers: { 'content-type': 'application/json' } },
        );
      }
      if (String(url) === 'https://s3.test/upload') {
        return new Response('AccessDenied', { status: 403 });
      }
      throw new Error('register no debería llamarse');
    });
    vi.stubGlobal('fetch', fetchMock);

    const file = new File([new Uint8Array([1])], 'doc.pdf', { type: 'application/pdf' });
    await expect(
      tramitesClient.uploadAttachment('inst-1', 'factura', file, 'tenant-1'),
    ).rejects.toThrow(/almacenamiento/i);
  });
});

/**
 * analyzeDocument: OCR semántico ANTES de subir. Multipart POST a través del API (no a S3);
 * devuelve el JSON extraído y, en PDFs multi-documento, el recorte en base64. Lanza si la respuesta
 * no es OK (proveedor caído/timeout) → el hook aborta la subida.
 */
describe('tramitesClient.analyzeDocument (OCR)', () => {
  afterEach(() => vi.restoreAllMocks());

  it('hace POST multipart a /ocr/{tipo} y devuelve el resultado parseado', async () => {
    const calls: Array<{ url: string; init?: RequestInit }> = [];
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      calls.push({ url: String(url), init });
      return new Response(
        JSON.stringify({
          ok: true,
          tipo: 'factura',
          data: { es_factura_valida: true, vehiculo_vin: 'ABC123' },
          extractedPdfBase64: null,
        }),
        { status: 200, headers: { 'content-type': 'application/json' } },
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    const file = new File([new Uint8Array([1, 2, 3])], 'doc.pdf', { type: 'application/pdf' });
    const result = await tramitesClient.analyzeDocument('factura', file, 'tenant-1');

    expect(result.ok).toBe(true);
    expect(result.tipo).toBe('factura');
    expect(result.data).toMatchObject({ es_factura_valida: true, vehiculo_vin: 'ABC123' });

    const call = calls[0]!;
    expect(call.url).toContain('/api/v1/tramites/ocr/factura');
    expect(call.init!.method).toBe('POST');
    const form = call.init!.body as FormData;
    expect([...form.keys()]).toContain('file');
  });

  it('lanza si el OCR no está disponible (503)', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('{"detail":"no disponible"}', { status: 503 })),
    );
    const file = new File([new Uint8Array([1])], 'doc.pdf', { type: 'application/pdf' });
    await expect(
      tramitesClient.analyzeDocument('impronta', file, 'tenant-1'),
    ).rejects.toThrow();
  });
});

/** Validaciones OCR del frontend (tipo + cruce VIN) y utilidades del flujo. */
describe('evaluateOcr / helpers OCR', () => {
  it('rechaza cuando es_valido/es_factura_valida es false', () => {
    expect(evaluateOcr({ es_valido: false }, null).rechazado).toBe(true);
    expect(evaluateOcr({ es_factura_valida: false }, 'ABC').rechazado).toBe(true);
  });

  it('el rechazo por tipo explica el porqué en vez de quedarse en la frase genérica', () => {
    // 1) lo que el propio OCR observó
    expect(
      evaluateOcr({ es_valido: false, observaciones: 'Es un anexo de nacionalización.' }, null).motivo,
    ).toContain('Es un anexo de nacionalización.');
    // 2) sin observaciones, al menos qué creyó que era
    expect(
      evaluateOcr({ es_valido: false, tipo_documento: 'certificado_importacion' }, null).motivo,
    ).toContain('certificado importacion');
    // 3) sin nada, un mensaje que no deja al operador a ciegas
    expect(evaluateOcr({ es_valido: false }, null).motivo).toContain('no se reconoció');
  });

  it('acepta cuando el trámite está DENTRO de un documento que ampara varios vehículos', () => {
    // Una declaración de importación cubre el lote entero del contenedor: comparar la cadena
    // completa rechazaría un documento legítimo sólo por traer a los demás vehículos.
    const lote = 'LRWYGCFJ0TC771798, LRWYGCFJ0TC771994, LRWYGCFJ0TC772031';
    expect(evaluateOcr({ es_valido: true, vehiculo_vin: lote }, 'LRWYGCFJ0TC771994').rechazado).toBe(
      false,
    );
    const fuera = evaluateOcr({ es_valido: true, vehiculo_vin: lote }, 'LRWYGCEK3TC767884');
    expect(fuera.rechazado).toBe(true);
    // …y el motivo no vomita la lista completa
    expect(fuera.motivo).toContain('y 1 más');
  });

  it('vinsDelDocumento parte la lista sin romper VIN con espacios o guiones', () => {
    expect(vinsDelDocumento('AAA-111, bbb 222')).toEqual(['AAA111', 'BBB222']);
    expect(vinsDelDocumento('')).toEqual([]);
  });

  it('resumirVins deja pasar listas cortas y recorta las largas', () => {
    expect(resumirVins('AAA, BBB')).toBe('AAA, BBB');
    expect(resumirVins('AAA, BBB, CCC, DDD')).toBe('AAA, BBB y 2 más');
  });

  it('rechaza cuando no hay datos', () => {
    expect(evaluateOcr(null, 'ABC').rechazado).toBe(true);
  });

  it('rechaza cuando el VIN del documento no coincide con el del trámite', () => {
    const r = evaluateOcr({ es_valido: true, vehiculo_vin: 'AAA111' }, 'BBB222');
    expect(r.rechazado).toBe(true);
    expect(r.motivo).toMatch(/VIN/i);
  });

  it('acepta cuando el VIN coincide normalizado (ignora guiones/espacios/mayúsculas)', () => {
    expect(
      evaluateOcr({ es_valido: true, vehiculo_vin: 'vin-123' }, 'VIN 123').rechazado,
    ).toBe(false);
    // usa vehiculo_chasis si no hay vehiculo_vin
    expect(
      evaluateOcr({ es_valido: true, vehiculo_chasis: 'CH1' }, 'ch 1').rechazado,
    ).toBe(false);
  });

  it('acepta cuando el documento es válido y no hay VIN que cruzar', () => {
    expect(evaluateOcr({ es_valido: true }, 'BBB').rechazado).toBe(false);
    expect(evaluateOcr({ es_valido: true, vehiculo_vin: 'AAA' }, null).rechazado).toBe(false);
  });

  it('rechaza cuando la API marca ok=false aunque el JSON traiga es_valido', () => {
    const r = evaluateOcr({ es_valido: true }, null, false);
    expect(r.rechazado).toBe(true);
    expect(r.motivo).toContain('no confirmó');
  });

  it('con ok=false y es_valido false usa el motivo de tipo', () => {
    expect(
      evaluateOcr({ es_valido: false, observaciones: 'Es una factura.' }, null, false).motivo,
    ).toContain('Es una factura.');
  });

  it('normalizeVin deja sólo alfanuméricos en mayúsculas', () => {
    expect(normalizeVin('abc-123 xyz')).toBe('ABC123XYZ');
    expect(normalizeVin(null)).toBe('');
  });

  // HU #12034 — el OCR de un documento ya no depende de la modalidad del trámite, sino de que el
  // backend declare que ese tipo tiene prompt.
  it('esTipoOcr resuelve por el código del documento, sin importar la modalidad', () => {
    const tipos = new Set(['factura', 'aduana', 'impronta']);

    expect(esTipoOcr(tipos, 'factura')).toBe(true);
    expect(esTipoOcr(tipos, 'aduana')).toBe(true);
    expect(esTipoOcr(tipos, 'otro')).toBe(false);
  });

  it('esTipoOcr no distingue mayúsculas', () => {
    expect(esTipoOcr(new Set(['soat']), 'SOAT')).toBe(true);
  });

  it('esTipoOcr falla ABIERTO cuando no se pudo consultar el backend', () => {
    // Devolver false dejaría el documento sin analizar y sin ningún error visible: es exactamente
    // el fallo silencioso que esta HU elimina. Se intenta, y si el tipo no tiene prompt el backend
    // lo rechaza sin bloquear la carga.
    expect(esTipoOcr(null, 'cualquier_cosa')).toBe(true);
  });

  it('base64ToPdfFile produce un File PDF con nombre .pdf', () => {
    const f = base64ToPdfFile(btoa('hello'), 'documento.png');
    expect(f).toBeInstanceOf(File);
    expect(f.type).toBe('application/pdf');
    expect(f.name).toBe('documento.pdf');
  });
});
