import { describe, it, expect } from 'vitest';

import {
  capacidadesEfectivas,
  decisionesDelTipoDePrenda,
  esFamiliaTraspaso,
  esTipoDePrenda,
  modalidadPorEntrada,
  modalidadPorPartes,
  rolesDeActores,
  transformacionDelTipo,
  permiteGenerarImprontaAutomatica,
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

describe('trámites complementarios (art. 5.1.8)', () => {
  it('la familia OTROS no acumula: ni transformaciones ni prenda por encima del tipo', () => {
    const caps = capacidadesEfectivas(
      { ...OTROS, allowsComplementaryTransformations: false, allowsComplementaryPrenda: false },
      'OTROS',
    );

    expect(caps.permiteTransformacionesComplementarias).toBe(false);
    expect(caps.permitePrendaComplementaria).toBe(false);
  });

  it('matrícula y traspaso conservan la acumulación', () => {
    expect(
      capacidadesEfectivas(TRASPASO, 'TRASPASO').permiteTransformacionesComplementarias,
    ).toBe(true);
    expect(capacidadesEfectivas(TRASPASO, 'TRASPASO').permitePrendaComplementaria).toBe(true);
    expect(
      capacidadesEfectivas(MATRICULA, 'MATRICULAS').permiteTransformacionesComplementarias,
    ).toBe(true);
  });

  it('sin la llave en las capacidades, decide la familia', () => {
    // Un borrador abierto antes de que estas llaves existieran no puede perder sus simultáneos:
    // leer la ausencia como `false` se los apagaría a un traspaso en curso.
    expect(capacidadesEfectivas(TRASPASO, 'TRASPASO').permiteTransformacionesComplementarias).toBe(true);
    expect(capacidadesEfectivas(OTROS, 'OTROS').permiteTransformacionesComplementarias).toBe(false);
  });

  it('la cancelación de matrícula no acumula nada, aunque su familia sí', () => {
    // Acumular presupone un vehículo que sigue inscrito; la cancelación lo saca del registro. Es la
    // excepción por TIPO que las llaves del perfil existen para declarar (DDL 93): sin ellas, el
    // asistente le pintaba «Asignación de Prenda» y «Trámites Simultáneos» por ser MATRICULAS.
    const caps = capacidadesEfectivas(
      {
        ...MATRICULA,
        allowsComplementaryTransformations: false,
        allowsComplementaryPrenda: false,
      },
      'MATRICULAS',
    );

    expect(caps.permitePrendaComplementaria).toBe(false);
    expect(caps.permiteTransformacionesComplementarias).toBe(false);
    // La excepción es de este tipo, no de la familia: la matrícula inicial conserva los suyos.
    expect(capacidadesEfectivas(MATRICULA, 'MATRICULAS').permitePrendaComplementaria).toBe(true);
  });

  it('el respaldo sin capacidades también distingue la familia', () => {
    expect(capacidadesEfectivas(null, 'OTROS').permitePrendaComplementaria).toBe(false);
    expect(capacidadesEfectivas(null, 'TRASPASO').permitePrendaComplementaria).toBe(true);
    expect(capacidadesEfectivas(null, 'MATRICULAS').permitePrendaComplementaria).toBe(true);
  });
});

describe('capa que le pertenece al tipo', () => {
  it('reconoce el atributo que cada tipo cambia por definición', () => {
    expect(transformacionDelTipo('CAMBIO_COLOR')).toBe('color');
    expect(transformacionDelTipo('CAMBIO_CARROCERIA')).toBe('carroceria');
    expect(transformacionDelTipo('CONVERSION_COMBUSTIBLE')).toBe('combustible');
    expect(transformacionDelTipo('BLINDAJE')).toBe('blindaje');
    expect(transformacionDelTipo('DUPLICADO_PLACA')).toBeNull();
    expect(transformacionDelTipo(null)).toBeNull();
  });

  it('distingue el trámite de prenda del gravamen añadido a otro trámite', () => {
    expect(esTipoDePrenda('PRENDA_INSCRIPCION')).toBe(true);
    expect(esTipoDePrenda('LEVANTAMIENTO_PRENDA')).toBe(true);
    expect(esTipoDePrenda('CAMBIO_ACREEDOR')).toBe(true);
    expect(esTipoDePrenda('TRASPASO_STANDARD')).toBe(false);
    expect(esTipoDePrenda('BLINDAJE')).toBe(false);
  });

  it('la decisión de un tipo prendario es fija: la eligió quien eligió el trámite', () => {
    // Ofrecer «omitir» en un levantamiento de prenda sería ofrecer no hacer el trámite que se radica.
    expect(decisionesDelTipoDePrenda('PRENDA_INSCRIPCION')).toEqual(['registrar']);
    expect(decisionesDelTipoDePrenda('LEVANTAMIENTO_PRENDA')).toEqual(['levantar']);
    expect(decisionesDelTipoDePrenda('LEVANTAR_INSCRIBIR_PRENDA')).toEqual(['levantar', 'registrar']);
    expect(decisionesDelTipoDePrenda('BLINDAJE')).toBeNull();
    expect(decisionesDelTipoDePrenda('TRASPASO_STANDARD')).toBeNull();
  });
});

describe('arrendatario (leasing)', () => {
  const LEASING: WizardCapabilities = { ...MATRICULA, requiresLessee: true };

  it('el locatario es una parte más que el tipo declara', () => {
    expect(capacidadesEfectivas(LEASING, 'MATRICULAS').pideLocatario).toBe(true);
    expect(rolesDeActores(capacidadesEfectivas(LEASING, 'MATRICULAS'))).toEqual([
      'comprador',
      'locatario',
    ]);
  });

  it('sin la llave no hay locatario en ningún tipo actual', () => {
    expect(capacidadesEfectivas(MATRICULA, 'MATRICULAS').pideLocatario).toBe(false);
    expect(capacidadesEfectivas(TRASPASO, 'TRASPASO').pideLocatario).toBe(false);
    expect(capacidadesEfectivas(OTROS, 'OTROS').pideLocatario).toBe(false);
    expect(capacidadesEfectivas(null, 'MATRICULAS').pideLocatario).toBe(false);
  });

  it('propietario y locatario se capturan JUNTOS, como vendedor y comprador', () => {
    // El catálogo los modela en un paso cada uno —el motor los exige por separado— pero la pantalla
    // los muestra lado a lado, que es como el gestor los compara.
    expect(rolesDeActores(capacidadesEfectivas(LEASING, 'MATRICULAS'))).toHaveLength(2);
    expect(rolesDeActores(capacidadesEfectivas(TRASPASO, 'TRASPASO'))).toHaveLength(2);
    // Un titular único sigue solo: matrícula y los OTROS sin arrendatario.
    expect(rolesDeActores(capacidadesEfectivas(MATRICULA, 'MATRICULAS'))).toHaveLength(1);
    expect(rolesDeActores(capacidadesEfectivas(OTROS, 'OTROS'))).toHaveLength(1);
  });
});

describe('vendedor que no se captura por formulario (ADR-0051)', () => {
  // TRASPASO_UNILATERAL: el locatario formaliza a su nombre, el propietario existe en el FUR y es
  // el único que firma y valida identidad, pero nunca pasa por el asistente.
  const UNILATERAL: WizardCapabilities = {
    ...TRASPASO,
    sellerCapturedViaForm: false,
    requiresCommercialValue: false,
    biometricActors: ['OWNER'],
  };

  it('hay parte vendedora, pero el paso de actores no le pinta formulario', () => {
    const caps = capacidadesEfectivas(UNILATERAL, 'TRASPASO');

    expect(caps.pideVendedor).toBe(true);
    expect(caps.vendedorCapturaPorFormulario).toBe(false);
    expect(rolesDeActores(caps)).toEqual(['comprador']);
  });

  it('sigue validando la identidad del propietario, que es el único que interviene', () => {
    expect(capacidadesEfectivas(UNILATERAL, 'TRASPASO').validaIdentidadDelVendedor).toBe(true);
  });

  it('sin la llave, todo tipo con vendedor lo sigue capturando por formulario', () => {
    // La llave nueva no puede apagarle el formulario a un traspaso estándar por no declararla.
    const caps = capacidadesEfectivas(TRASPASO, 'TRASPASO');

    expect(caps.vendedorCapturaPorFormulario).toBe(true);
    expect(rolesDeActores(caps)).toEqual(['vendedor', 'comprador']);
  });

  it('un tipo SIN parte vendedora no infla la captura por la mera ausencia de la llave', () => {
    // Si `vendedorCapturaPorFormulario` cayera a `true` por defecto, `rolesDeActores()` —que ya no
    // mira `pideVendedor`— le pintaría un formulario de vendedor a una matrícula.
    expect(capacidadesEfectivas(MATRICULA, 'MATRICULAS').vendedorCapturaPorFormulario).toBe(false);
    expect(rolesDeActores(capacidadesEfectivas(MATRICULA, 'MATRICULAS'))).toEqual(['comprador']);
  });

  it('el respaldo sin capacidades nunca tiene captura oculta', () => {
    // Las dos modalidades heredadas son anteriores a TRASPASO_UNILATERAL: ahí la captura del
    // vendedor siempre coincide con que exista parte vendedora.
    const traspaso = capacidadesEfectivas(null, 'TRASPASO');
    expect(traspaso.vendedorCapturaPorFormulario).toBe(true);
    expect(rolesDeActores(traspaso)).toEqual(['vendedor', 'comprador']);
    expect(capacidadesEfectivas(null, 'MATRICULAS').vendedorCapturaPorFormulario).toBe(false);
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

describe('generación automática de impronta', () => {
  it('MANUAL la apaga; el resto (o ausente) la permite', () => {
    expect(permiteGenerarImprontaAutomatica('MANUAL')).toBe(false);
    expect(permiteGenerarImprontaAutomatica('manual')).toBe(false);
    expect(permiteGenerarImprontaAutomatica('AUTO')).toBe(true);
    expect(permiteGenerarImprontaAutomatica('OPERATOR_CHOICE')).toBe(true);
    expect(permiteGenerarImprontaAutomatica(null)).toBe(true);
    expect(permiteGenerarImprontaAutomatica(undefined)).toBe(true);
  });

  it('viaja en las capacidades efectivas', () => {
    expect(
      capacidadesEfectivas({ ...TRASPASO, improntaSource: 'MANUAL' }, 'TRASPASO')
        .permiteGenerarImprontaAutomatica,
    ).toBe(false);
    expect(capacidadesEfectivas(TRASPASO, 'TRASPASO').permiteGenerarImprontaAutomatica).toBe(true);
    expect(capacidadesEfectivas(null, 'TRASPASO').permiteGenerarImprontaAutomatica).toBe(true);
  });
});
