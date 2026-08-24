import { describe, it, expect } from 'vitest';

import {
  capacidadesEfectivas,
  esFamiliaTraspaso,
  modalidadPorEntrada,
  modalidadPorPartes,
  rolesDeActores,
} from '../wizardCapabilities';
import type { WizardCapabilities } from '@/lib/api/types/procedure-runtime';

/**
 * ADR-0050 — el asistente deja de decidir por modalidad. Estas pruebas fijan la traducción de
 * capacidades a decisiones de render, incluido el respaldo para los borradores que aún no traen
 * capacidades: ninguno de ellos puede cambiar de comportamiento.
 */
const TRASPASO: WizardCapabilities = {
  entryMode: 'PLATE',
  requiresSeller: true,
  requiresBuyer: true,
  allowsMultipleBuyer: true,
  requiresCommercialValue: true,
  requiresBiometrics: true,
  biometricActors: ['OWNER', 'BUYER'],
  hasPrendaGate: true,
};

const MATRICULA: WizardCapabilities = {
  entryMode: 'VIN',
  requiresSeller: false,
  requiresBuyer: true,
  allowsMultipleBuyer: false,
  requiresCommercialValue: false,
  requiresBiometrics: true,
  biometricActors: ['BUYER'],
  hasPrendaGate: false,
};

const OTROS: WizardCapabilities = { ...MATRICULA, entryMode: 'PLATE' };

describe('capacidadesEfectivas', () => {
  it('un trámite de OTROS entra por placa y captura un solo titular', () => {
    const caps = capacidadesEfectivas(OTROS, 'OTROS');

    expect(caps.entraPorVin).toBe(false);
    expect(caps.pideVendedor).toBe(false);
    expect(caps.pideValorComercial).toBe(false);
    expect(rolesDeActores(caps)).toEqual(['comprador']);
  });

  it('el traspaso conserva sus dos partes, el valor comercial y la puerta de prenda', () => {
    const caps = capacidadesEfectivas(TRASPASO, 'TRASPASO');

    expect(rolesDeActores(caps)).toEqual(['vendedor', 'comprador']);
    expect(caps.pideValorComercial).toBe(true);
    expect(caps.prendaEsPuerta).toBe(true);
    expect(caps.validaIdentidadDelVendedor).toBe(true);
  });

  it('sin OWNER en la biométrica no se valida la identidad de una parte que no interviene', () => {
    expect(capacidadesEfectivas(OTROS, 'OTROS').validaIdentidadDelVendedor).toBe(false);
  });

  it('la matrícula es el único caso que entra por VIN', () => {
    expect(capacidadesEfectivas(MATRICULA, 'MATRICULAS').entraPorVin).toBe(true);
    expect(capacidadesEfectivas(TRASPASO, 'TRASPASO').entraPorVin).toBe(false);
  });

  describe('respaldo sin capacidades (borradores abiertos antes del cambio)', () => {
    it('reproduce exactamente las dos ramas anteriores', () => {
      const traspaso = capacidadesEfectivas(null, 'TRASPASO');
      expect(traspaso.pideVendedor).toBe(true);
      expect(traspaso.pideValorComercial).toBe(true);
      expect(traspaso.entraPorVin).toBe(false);

      const matricula = capacidadesEfectivas(null, 'MATRICULAS');
      expect(matricula.pideVendedor).toBe(false);
      expect(matricula.entraPorVin).toBe(true);
    });

    it('acepta el vocabulario heredado además de la familia', () => {
      // El estado del asistente trae `TRASPASO` en un campo que se sigue llamando `modalidad`; la
      // vía de entrada podía traer `traspaso`. Comparar contra una sola forma dejaba la rama muerta.
      expect(capacidadesEfectivas(null, 'traspaso').pideVendedor).toBe(true);
      expect(capacidadesEfectivas(null, 'matricula_inicial').entraPorVin).toBe(true);
    });

    it('OTROS no hereda la entrada por VIN de la matrícula', () => {
      // Es el caso que el respaldo binario no sabía representar: no es traspaso, luego era matrícula.
      expect(capacidadesEfectivas(null, 'OTROS').entraPorVin).toBe(false);
    });
  });
});

describe('adaptadores a la modalidad heredada', () => {
  it('lo que depende de si el vehículo ya está matriculado usa la entrada', () => {
    expect(modalidadPorEntrada(capacidadesEfectivas(MATRICULA, 'MATRICULAS'))).toBe('matricula_inicial');
    // Un blindaje no pide factura ni aduana ni elige organismo: va del lado del vehículo ya inscrito.
    expect(modalidadPorEntrada(capacidadesEfectivas(OTROS, 'OTROS'))).toBe('traspaso');
  });

  it('lo que depende de cuántas partes intervienen usa las partes', () => {
    expect(modalidadPorPartes(capacidadesEfectivas(TRASPASO, 'TRASPASO'))).toBe('traspaso');
    // Aquí OTROS sí va del lado de la parte única: solo hay un titular que validar.
    expect(modalidadPorPartes(capacidadesEfectivas(OTROS, 'OTROS'))).toBe('matricula_inicial');
  });
});

describe('esFamiliaTraspaso', () => {
  it('reconoce las dos escrituras del mismo dato', () => {
    expect(esFamiliaTraspaso('TRASPASO')).toBe(true);
    expect(esFamiliaTraspaso('traspaso')).toBe(true);
    expect(esFamiliaTraspaso('  Traspaso  ')).toBe(true);
  });

  it('no confunde las demás familias', () => {
    expect(esFamiliaTraspaso('MATRICULAS')).toBe(false);
    expect(esFamiliaTraspaso('OTROS')).toBe(false);
    expect(esFamiliaTraspaso(null)).toBe(false);
  });
});
