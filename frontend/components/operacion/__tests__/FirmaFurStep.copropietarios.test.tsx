// Múltiple Propietario (ADR-0053) — `CopropietariosEstadoSection` es la pieza de `FirmaFurStep.tsx`
// que cierra la brecha de solo lectura: "el trámite no avanza hasta que TODOS validen y firmen", así
// que el gestor tiene que poder ver A QUIÉN LE FALTA, no un agregado por lado que puede decir "falta
// 1" con 3 pendientes. Se prueba en AISLAMIENTO (sin montar `FirmaFurStep` completo, que dispara ~8
// llamadas de red al montar y no tiene arnés propio): la lógica de estado por actor ya está cubierta
// exhaustivamente sin RTL en `lib/tramites/__tests__/ownership-share.test.ts`
// (`identityStatusForActor`, `actorsOrderedByOrdinal`); este archivo verifica que el componente los
// conecta correctamente al DOM.
import { describe, it, expect } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import type {
  BiometricValidation,
  FirmaBaulActorCoberturaDto,
  ProcedureActor,
} from '@/lib/api/types/procedure-runtime';
import { CopropietariosEstadoSection } from '@/components/operacion/FirmaFurStep';

function comprador(overrides: Partial<ProcedureActor> = {}): ProcedureActor {
  return {
    rol: 'comprador',
    tipoDocumento: 'CC',
    numeroDocumento: '111',
    nombreCompleto: 'Comprador Uno',
    email: 'uno@example.com',
    ...overrides,
  };
}

function bio(overrides: Partial<BiometricValidation> = {}): BiometricValidation {
  return {
    id: 'bio-1',
    partyRole: 'comprador',
    name: 'Comprador Uno',
    documentType: 'CC',
    documentNumber: '111',
    email: 'uno@example.com',
    status: 'aprobado',
    intentos: 1,
    maxIntentos: 3,
    score: 0.98,
    expiresAt: '2027-01-01T00:00:00Z',
    validatedAt: '2026-01-01T00:00:00Z',
    expired: false,
    provider: 'kyverum',
    captureUrl: null,
    ...overrides,
  };
}

const NO_BAUL: FirmaBaulActorCoberturaDto[] = [];

describe('CopropietariosEstadoSection — Múltiple Propietario', () => {
  it('con un solo actor por lado, no pinta nada (cero regresión: el resumen embebido ya lo cubre)', () => {
    render(
      <CopropietariosEstadoSection
        titulo="Comprador"
        actores={[comprador()]}
        biometric={[]}
        firmaBaulActores={NO_BAUL}
      />,
    );
    expect(screen.queryByRole('heading')).toBeNull();
    expect(screen.queryByRole('list')).toBeNull();
  });

  it('lista vacía no pinta nada', () => {
    const { container } = render(
      <CopropietariosEstadoSection titulo="Comprador" actores={[]} biometric={[]} firmaBaulActores={NO_BAUL} />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('con 2+ copropietarios: uno a uno, ordenados por ordinal, cada uno con SU estado', () => {
    // Orden deliberadamente invertido en el array — la sección debe reordenar por `ordinal`.
    const actores = [
      comprador({
        numeroDocumento: '222',
        nombreCompleto: 'Copropietario Dos',
        ordinal: 2,
        porcentaje: 30,
      }),
      comprador({
        numeroDocumento: '111',
        nombreCompleto: 'Copropietario Uno',
        ordinal: 1,
        porcentaje: 70,
      }),
    ];
    const biometric = [bio({ documentNumber: '111', status: 'aprobado' })];
    // El copropietario 2 NO tiene fila biométrica propia — debe verse "Pendiente", NO heredar el
    // "aprobado" del copropietario 1 (ese es exactamente el "falta 1 de 3" que se corrige aquí).

    render(
      <CopropietariosEstadoSection
        titulo="Comprador"
        actores={actores}
        biometric={biometric}
        firmaBaulActores={NO_BAUL}
      />,
    );

    const items = screen.getAllByRole('listitem');
    expect(items).toHaveLength(2);

    // Orden: Comprador 1 primero (aunque llegó segundo en el array).
    expect(within(items[0]).getByText(/Comprador 1 · Copropietario Uno/)).toBeInTheDocument();
    expect(within(items[0]).getByText(/70%/)).toBeInTheDocument();
    expect(within(items[0]).getByText('Identidad aprobada')).toBeInTheDocument();

    expect(within(items[1]).getByText(/Comprador 2 · Copropietario Dos/)).toBeInTheDocument();
    expect(within(items[1]).getByText(/30%/)).toBeInTheDocument();
    expect(within(items[1]).getByText('Pendiente')).toBeInTheDocument();
    expect(within(items[1]).queryByText('Identidad aprobada')).toBeNull();
  });

  it('3 copropietarios pendientes: la lista muestra los 3 "Pendiente" (no un agregado que oculte a 2)', () => {
    const actores = [
      comprador({ numeroDocumento: '1', nombreCompleto: 'Uno', ordinal: 1, porcentaje: 34 }),
      comprador({ numeroDocumento: '2', nombreCompleto: 'Dos', ordinal: 2, porcentaje: 33 }),
      comprador({ numeroDocumento: '3', nombreCompleto: 'Tres', ordinal: 3, porcentaje: 33 }),
    ];

    render(
      <CopropietariosEstadoSection
        titulo="Comprador"
        actores={actores}
        biometric={[]}
        firmaBaulActores={NO_BAUL}
      />,
    );

    expect(screen.getAllByText('Pendiente')).toHaveLength(3);
  });

  it('rechazado y aprobado conviven sin mezclarse entre copropietarios', () => {
    const actores = [
      comprador({ numeroDocumento: '1', nombreCompleto: 'Aprobado', ordinal: 1, porcentaje: 50 }),
      comprador({ numeroDocumento: '2', nombreCompleto: 'Rechazado', ordinal: 2, porcentaje: 50 }),
    ];
    const biometric = [
      bio({ documentNumber: '1', status: 'aprobado' }),
      bio({ documentNumber: '2', status: 'rechazado' }),
    ];

    render(
      <CopropietariosEstadoSection
        titulo="Comprador"
        actores={actores}
        biometric={biometric}
        firmaBaulActores={NO_BAUL}
      />,
    );

    expect(screen.getByText('Identidad aprobada')).toBeInTheDocument();
    expect(screen.getByText('Identidad rechazada')).toBeInTheDocument();
  });

  it('usa el rótulo del catálogo en el título de la lista y de cada fila', () => {
    const actores = [
      comprador({ numeroDocumento: '1', ordinal: 1, porcentaje: 60 }),
      comprador({ numeroDocumento: '2', ordinal: 2, porcentaje: 40 }),
    ];
    render(
      <CopropietariosEstadoSection
        titulo="Locatario"
        actores={actores}
        biometric={[]}
        firmaBaulActores={NO_BAUL}
      />,
    );
    expect(screen.getByText('Copropietarios — Locatario')).toBeInTheDocument();
    expect(screen.getByText(/Locatario 1 ·/)).toBeInTheDocument();
    expect(screen.getByText(/Locatario 2 ·/)).toBeInTheDocument();
  });

  // ADR-0053 — antes de este cierre, la cobertura del baúl solo se conocía por LADO
  // (`firmaBaulPartes`): con dos copropietarios jurídicos, si UNO tenía baúl vigente, la aproximación
  // marcaba a AMBOS como cubiertos. `firmaBaulActores` es el dato real, por documento del
  // representante legal + ordinal — este es el caso exacto que antes no se podía distinguir.
  it('dos copropietarios jurídicos, uno cubierto por el baúl y el otro no: cada uno con su estado correcto', () => {
    const actores = [
      comprador({
        tipoDocumento: 'NIT',
        numeroDocumento: '900111222',
        nombreCompleto: 'Compañía Uno SAS',
        personType: 'juridical',
        ordinal: 1,
        porcentaje: 50,
        representanteLegal: { tipoDocumento: 'CC', numeroDocumento: '80100100' },
      }),
      comprador({
        tipoDocumento: 'NIT',
        numeroDocumento: '900333444',
        nombreCompleto: 'Compañía Dos SAS',
        personType: 'juridical',
        ordinal: 2,
        porcentaje: 50,
        representanteLegal: { tipoDocumento: 'CC', numeroDocumento: '80200200' },
      }),
    ];
    // Solo el ordinal=1 tiene baúl vigente (documento del SU representante legal, no el NIT de
    // ninguna de las dos compañías) — el ordinal=2 no aparece en la lista en absoluto.
    const firmaBaulActores: FirmaBaulActorCoberturaDto[] = [
      { parte: 'comprador', documentNumber: '80100100', ordinal: 1 },
    ];

    render(
      <CopropietariosEstadoSection
        titulo="Comprador"
        actores={actores}
        biometric={[]}
        firmaBaulActores={firmaBaulActores}
      />,
    );

    const items = screen.getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(within(items[0]).getByText(/Comprador 1 · Compañía Uno SAS/)).toBeInTheDocument();
    expect(within(items[0]).getByText('Firma del baúl')).toBeInTheDocument();

    expect(within(items[1]).getByText(/Comprador 2 · Compañía Dos SAS/)).toBeInTheDocument();
    expect(within(items[1]).getByText('Pendiente')).toBeInTheDocument();
    expect(within(items[1]).queryByText('Firma del baúl')).toBeNull();
  });
});
