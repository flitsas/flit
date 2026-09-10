// Bug #11629 — el selector de carrocería quedaba vacío cuando la clase que
// devuelve el RUNT no coincidía exactamente (por tildes o por variante
// ortográfica) con las claves del catálogo generado de carroceria.xlsx.
//
// Uso de ejemplo:
//   normalizeVehicleClass('Camión') → 'CAMION'
//   getBodyworksForVehicleClass('SEMIRREMOLQUE') → opciones de 'SEMIREMOLQUE'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  findBodyworkName,
  getBodyworksForVehicleClass,
  normalizeVehicleClass,
} from '../bodywork-by-class';

describe('normalizeVehicleClass', () => {
  it('AC1 — clase exacta se conserva tal cual (trim + upper)', () => {
    expect(normalizeVehicleClass('AUTOMOVIL')).toBe('AUTOMOVIL');
  });

  it('AC1 — clase con tilde pierde el diacrítico', () => {
    expect(normalizeVehicleClass('CAMIÓN')).toBe('CAMION');
    expect(normalizeVehicleClass('AUTOMÓVIL')).toBe('AUTOMOVIL');
    expect(normalizeVehicleClass('MICROBÚS')).toBe('MICROBUS');
  });

  it('AC1 — clase con espacios extra se recorta y los internos se colapsan', () => {
    expect(normalizeVehicleClass('  camion  ')).toBe('CAMION');
    expect(normalizeVehicleClass('CAMION   DE  CARGA')).toBe('CAMION DE CARGA');
  });

  it('clase vacía o nula retorna string vacío', () => {
    expect(normalizeVehicleClass('')).toBe('');
    expect(normalizeVehicleClass(null)).toBe('');
    expect(normalizeVehicleClass(undefined)).toBe('');
  });
});

describe('getBodyworksForVehicleClass', () => {
  let warnSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    warnSpy.mockRestore();
  });

  it('AC1 — clase exacta retorna las opciones del catálogo', () => {
    const options = getBodyworksForVehicleClass('AUTOMOVIL');
    expect(options.length).toBeGreaterThan(0);
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('AC1 — clase con tilde resuelve contra la clave sin tilde del catálogo', () => {
    const conTilde = getBodyworksForVehicleClass('CAMIÓN');
    const sinTilde = getBodyworksForVehicleClass('CAMION');
    expect(conTilde).toEqual(sinTilde);
    expect(conTilde.length).toBeGreaterThan(0);
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('AC1 — SEMIRREMOLQUE (ortografía RUNT) resuelve contra la clave SEMIREMOLQUE del catálogo', () => {
    const porAlias = getBodyworksForVehicleClass('SEMIRREMOLQUE');
    const porClaveOriginal = getBodyworksForVehicleClass('SEMIREMOLQUE');
    expect(porAlias.length).toBeGreaterThan(0);
    expect(porAlias).toEqual(porClaveOriginal);
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('AC1 — clase con espacios adicionales también resuelve', () => {
    const options = getBodyworksForVehicleClass('  CAMIONETA  ');
    expect(options).toEqual(getBodyworksForVehicleClass('CAMIONETA'));
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('clase vacía retorna lista vacía sin dejar traza (no es un fallo de resolución)', () => {
    expect(getBodyworksForVehicleClass('')).toEqual([]);
    expect(getBodyworksForVehicleClass(null)).toEqual([]);
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('AC2 — clase desconocida retorna lista vacía y deja traza con el valor crudo recibido', () => {
    const result = getBodyworksForVehicleClass('CLASE-INEXISTENTE');
    expect(result).toEqual([]);
    expect(warnSpy).toHaveBeenCalledTimes(1);
    expect(warnSpy).toHaveBeenCalledWith(
      '[bodywork-by-class] clase de vehículo sin match en el catálogo',
      expect.objectContaining({ raw: 'CLASE-INEXISTENTE', normalized: 'CLASE-INEXISTENTE' }),
    );
  });

  it('AC3 — no inventa carrocerías: clase sin match nunca devuelve opciones', () => {
    const result = getBodyworksForVehicleClass('ANFIBIO');
    expect(result).toEqual([]);
  });

  it('no lista el catálogo completo: AUTOMOVIL no incluye carrocerías de CAMION', () => {
    const auto = getBodyworksForVehicleClass('AUTOMOVIL').map((o) => o.name);
    expect(auto).toContain('SEDAN');
    expect(auto).not.toContain('ESTACAS');
    expect(auto.length).toBeLessThan(getBodyworksForVehicleClass('CAMION').length);
  });

  it('CAMION no incluye carrocerías de AUTOMOVIL (SEDAN)', () => {
    const camion = getBodyworksForVehicleClass('CAMION').map((o) => o.name);
    expect(camion).toContain('ESTACAS');
    expect(camion).not.toContain('SEDAN');
  });

  it('clase RUNT con calificativo resuelve la clave de catálogo (CAMION CISTERNA → CAMION)', () => {
    const cisterna = getBodyworksForVehicleClass('CAMION CISTERNA').map((o) => o.name);
    expect(cisterna).toEqual(getBodyworksForVehicleClass('CAMION').map((o) => o.name));
    expect(cisterna).not.toContain('SEDAN');
  });
});

describe('findBodyworkName — mismo tratamiento vía getBodyworksForVehicleClass', () => {
  it('resuelve nombre de carrocería aunque la clase venga acentuada', () => {
    const options = getBodyworksForVehicleClass('CAMION');
    expect(options.length).toBeGreaterThan(0);
    const code = options[0].code;
    expect(findBodyworkName(code, 'CAMIÓN')).toBe(options[0].name);
  });

  it('código/nombre vacío retorna null', () => {
    expect(findBodyworkName('', 'AUTOMOVIL')).toBeNull();
    expect(findBodyworkName(null, 'AUTOMOVIL')).toBeNull();
  });
});
