import { describe, it, expect } from 'vitest';
import { documentLabel } from '@/lib/tramites/document-labels';

// FEATURE 05 — nombre amigable de los documentos del trámite (listas de "Documentos generados"
// y "Documentos del expediente"). El certificado RNMC debe verse como la compraventa y el FUR.
describe('documentLabel', () => {
  it('mapea los tipos conocidos a su nombre amigable', () => {
    expect(documentLabel('fur')).toBe('FUR');
    expect(documentLabel('compraventa')).toBe('Formato de compraventa');
    expect(documentLabel('certificado_rnmc')).toBe('Certificado RNMC (medidas correctivas)');
    expect(documentLabel('certificado_identidad')).toBe('Certificado de identidad');
    expect(documentLabel('certificado_identidad_vendedor')).toBe('Certificado de identidad (vendedor)');
    expect(documentLabel('certificado_rues')).toBe('Certificado RUES');
    expect(documentLabel('consolidado')).toBe('Consolidado');
    expect(documentLabel('impronta')).toBe('Improntas');
  });

  it('humaniza un tipo desconocido (capitalize por palabra)', () => {
    expect(documentLabel('otro_documento_raro')).toBe('Otro Documento Raro');
    expect(documentLabel('factura')).toBe('Factura');
  });
});
