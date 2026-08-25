import { describe, it, expect } from 'vitest';

import { resolveActorRole, resolveStepBody } from '../sectionRendererRegistry';

/**
 * ADR-0050 / CFD-09 — el cuerpo del paso lo decide el `section_type` parametrizado, no la clave.
 * Es lo que hace que un tipo nuevo se dibuje en cuanto está parametrizado, en vez de caer en el
 * default vacío por no llamarse como los pasos de matrícula o traspaso.
 */
describe('resolveStepBody', () => {
  it('la decisión de prenda tiene cuerpo PROPIO', () => {
    // Caía en `documentos`, y como los tipos de prenda de la familia OTROS traen los dos pasos, el
    // asistente pintaba el paso de documentos entero donde el gestor esperaba el gravamen.
    expect(resolveStepBody({ key: 'prenda', sectionType: 'prenda_decision' })).toBe('prenda');
  });

  it('un borrador antiguo sin sectionType cae por la clave heredada', () => {
    expect(resolveStepBody({ key: 'prenda', sectionType: undefined })).toBe('prenda');
    expect(resolveStepBody({ key: 'propietario', sectionType: undefined })).toBe('actores');
    expect(resolveStepBody({ key: 'comercial', sectionType: undefined })).toBe('documentos');
  });

  it('matrícula y traspaso conservan sus cuerpos (regresión)', () => {
    expect(resolveStepBody({ key: 'consulta_vin', sectionType: 'vehicle_query' })).toBe('consulta');
    expect(resolveStepBody({ key: 'comprador', sectionType: 'actor_form' })).toBe('actores');
    expect(resolveStepBody({ key: 'vendedor', sectionType: 'actor_form' })).toBe('actores');
    expect(resolveStepBody({ key: 'documentos', sectionType: 'document_checklist' })).toBe('documentos');
    // Los datos comerciales viven DENTRO del paso de requisitos: no tienen cuerpo propio.
    expect(resolveStepBody({ key: 'documentos', sectionType: 'commercial' })).toBe('documentos');
    expect(resolveStepBody({ key: 'identidad', sectionType: 'biometric' })).toBe('identidad');
    expect(resolveStepBody({ key: 'fur', sectionType: 'signature_fur' })).toBe('fur');
  });

  it('un paso sin renderizador conocido no revienta: cae en genérico', () => {
    expect(resolveStepBody({ key: 'paso_inventado', sectionType: undefined })).toBe('generico');
  });
});

describe('resolveActorRole', () => {
  it('el titular de la familia OTROS se persiste como comprador aunque su paso sea «propietario»', () => {
    // El modelo no tiene rol 'propietario': el título es lo que lee el operador y el rol es lo que
    // el motor usa para saber a quién exigir.
    expect(resolveActorRole({ key: 'propietario' })).toBe('comprador');
  });

  it('solo el paso del vendedor captura la parte saliente', () => {
    expect(resolveActorRole({ key: 'vendedor' })).toBe('vendedor');
    expect(resolveActorRole({ key: 'comprador' })).toBe('comprador');
  });

  it('el arrendatario tiene rol propio: no se disfraza de comprador', () => {
    // Era el default silencioso que colapsaba al locatario contra el propietario.
    expect(resolveActorRole({ key: 'locatario' })).toBe('locatario');
  });
});
