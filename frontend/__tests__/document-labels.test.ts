import { describe, it, expect } from 'vitest';
import {
  documentLabel,
  catalogDocumentName,
  catalogDocumentTitle,
  gestorDocumentDisplayName,
} from '@/lib/tramites/document-labels';

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

describe('catalogDocumentName', () => {
  it('usa el nombre definido al crear el tipo, no una etiqueta hardcodeada', () => {
    expect(catalogDocumentName('paz_salvo', 'Paz y Salvo de Impuestos')).toBe(
      'Paz y Salvo de Impuestos',
    );
    expect(catalogDocumentName('cert_tradicion', 'Certificado de tradición')).toBe(
      'Certificado de tradición',
    );
    expect(catalogDocumentName('compraventa', 'Formato de Compraventa')).toBe(
      'Formato de Compraventa',
    );
    expect(catalogDocumentName('soat', 'SOAT (vigente)')).toBe('SOAT (vigente)');
  });

  it('cae a documentLabel si no hay nombre de catálogo', () => {
    expect(catalogDocumentName('fur')).toBe('FUR');
  });
});

describe('catalogDocumentTitle', () => {
  it('agrega el código entre paréntesis', () => {
    expect(catalogDocumentTitle('paz_salvo', 'Paz y Salvo de Impuestos')).toBe(
      'Paz y Salvo de Impuestos (paz_salvo)',
    );
  });
});

describe('gestorDocumentDisplayName', () => {
  it('es el nombre del catálogo', () => {
    expect(gestorDocumentDisplayName('paz_salvo', 'Paz y Salvo de Impuestos')).toBe(
      'Paz y Salvo de Impuestos',
    );
    expect(gestorDocumentDisplayName('DOCA', 'Documento A')).toBe('Documento A');
  });
});
