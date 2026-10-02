// Bug #13194 (P3) — salida del gestor en `asignado` cuando el RUNT no reporta el SOAT: cargar el PDF,
// registrar su lectura (ocr-fields tipo soat) y re-verificar antes de reintentar «Enviar al OT».
// Uso de ejemplo:
//   const r = await cargarSoporteSoatAsignado(tramitesClient, 'inst-1', pdf);
//   r.estado === 'vigente' → el gestor ya puede «Enviar al OT».
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { cargarSoporteSoatAsignado, type SoporteSoatClient } from '../soporte-soat-asignado';

function apiError(status: number, title: string, message = title) {
  return Object.assign(new Error(message), { status, problem: { title, status } });
}

const pdf = () => new File(['%PDF-1.4'], 'soat.pdf', { type: 'application/pdf' });

let client: { [K in keyof SoporteSoatClient]: ReturnType<typeof vi.fn> };
let orden: string[];

beforeEach(() => {
  orden = [];
  client = {
    analyzeDocument: vi.fn(async () => {
      orden.push('ocr');
      return { ok: true, tipo: 'soat', data: { fecha_vencimiento: '2027-05-01', numero_poliza: 'P-1' } };
    }),
    uploadAttachment: vi.fn(async () => {
      orden.push('upload');
      return { id: 'att-1' };
    }),
    persistOcrFields: vi.fn(async () => {
      orden.push('ocr-fields');
      return { persistidos: 2 };
    }),
    validateSoatViaRunt: vi.fn(async () => {
      orden.push('validate');
      return {
        vigente: true,
        soatEstado: 'vigente',
        vencimiento: null,
        aseguradora: null,
        message: 'El RUNT no reporta el SOAT, pero el soporte cargado está vigente: se conserva.',
      };
    }),
  };
});

const run = (tenantId?: string) =>
  cargarSoporteSoatAsignado(client as unknown as SoporteSoatClient, 'inst-1', pdf(), tenantId);

describe('cargarSoporteSoatAsignado', () => {
  it('happy path: OCR → sube el adjunto ANTES de ocr-fields (el backend exige el adjunto) → re-verifica', async () => {
    const r = await run('t-1');
    expect(orden).toEqual(['ocr', 'upload', 'ocr-fields', 'validate']);
    expect(client.uploadAttachment).toHaveBeenCalledWith('inst-1', 'soat', expect.any(File), 't-1');
    expect(client.persistOcrFields).toHaveBeenCalledWith(
      'inst-1',
      'soat',
      { fecha_vencimiento: '2027-05-01', numero_poliza: 'P-1' },
      't-1',
    );
    expect(r.estado).toBe('vigente');
    expect(r.mensaje).toMatch(/Ya puedes enviar el trámite al OT/);
  });

  it('sube el recorte del PDF cuando el OCR lo devuelve', async () => {
    client.analyzeDocument.mockResolvedValue({
      ok: true,
      tipo: 'soat',
      data: { fecha_vencimiento: '2027-05-01' },
      extractedPdfBase64: btoa('%PDF-recorte'),
    });
    await run();
    const subido = client.uploadAttachment.mock.calls[0][2] as File;
    expect(subido.type).toBe('application/pdf');
    expect(subido.size).toBe('%PDF-recorte'.length);
  });

  it('OCR caído o sin datos: sube igual pero avisa que sin fecha legible no cuenta (no llama ocr-fields)', async () => {
    client.analyzeDocument.mockRejectedValue(new Error('503'));
    const r = await run();
    expect(client.uploadAttachment).toHaveBeenCalled();
    expect(client.persistOcrFields).not.toHaveBeenCalled();
    expect(r.estado).toBe('sin_lectura');
    expect(r.mensaje).toMatch(/no se pudo leer su fecha de vencimiento/);
  });

  it('409 soporte_soat_requerido de ocr-fields → mensaje claro', async () => {
    client.persistOcrFields.mockRejectedValue(apiError(409, 'soporte_soat_requerido'));
    const r = await run();
    expect(r.estado).toBe('error');
    expect(r.mensaje).toMatch(/El PDF del SOAT no quedó registrado en el trámite/);
    expect(client.validateSoatViaRunt).not.toHaveBeenCalled();
  });

  it('502 del RUNT al re-verificar → «El RUNT no respondió» (el soporte quedó registrado)', async () => {
    client.validateSoatViaRunt.mockRejectedValue(
      apiError(502, 'Bad Gateway', 'El RUNT no respondió. Intenta de nuevo o carga el PDF del SOAT.'),
    );
    const r = await run();
    expect(r.estado).toBe('runt_no_respondio');
    expect(r.mensaje).toMatch(/^El RUNT no respondió/);
    expect(r.mensaje).toMatch(/soporte del SOAT quedó registrado/);
  });

  it('el soporte no acredita vigencia (vencido o fecha ilegible) → no_vigente', async () => {
    client.validateSoatViaRunt.mockResolvedValue({
      vigente: false,
      soatEstado: 'unknown',
      vencimiento: null,
      aseguradora: null,
      message: 'El RUNT no reporta un SOAT vigente para el vehículo. Carga el PDF del SOAT para continuar.',
    });
    const r = await run();
    expect(r.estado).toBe('no_vigente');
    expect(r.mensaje).toMatch(/no acredita un SOAT vigente/);
  });

  it('fallo al subir el adjunto → error y no sigue', async () => {
    client.uploadAttachment.mockRejectedValue(new Error('El archivo supera el tamaño máximo.'));
    const r = await run();
    expect(r).toEqual({ estado: 'error', mensaje: 'El archivo supera el tamaño máximo.' });
    expect(client.persistOcrFields).not.toHaveBeenCalled();
  });
});
