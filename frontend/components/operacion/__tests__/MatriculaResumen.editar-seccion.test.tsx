// Botón «Editar» de las tarjetas del Resumen: en un borrador editable, cada sección lleva al paso
// donde se captura. Sin resolvedor (o con destino `null`) la tarjeta se queda en solo lectura.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import MatriculaResumen, { type ResolverEdicion } from '@/components/operacion/MatriculaResumen';
import ExpedienteVisor from '@/components/operacion/ExpedienteVisor';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getInstance: vi.fn(() => Promise.resolve({ fieldValues: [] })),
    getActors: vi.fn(() => Promise.resolve([])),
    getBiometricState: vi.fn(() => Promise.resolve(null)),
    ensureIdentity: vi.fn(() => Promise.resolve({ outcome: 'requiere_validacion' })),
  },
}));

function renderResumen(resolverEdicion?: ResolverEdicion) {
  return render(
    <MatriculaResumen
      modalidad="traspaso"
      status="borrador"
      placa="ABC123"
      vehiculo="YAMAHA 2026"
      vin="VIN123"
      vendedor={{ nombre: 'Luis Dos', documento: '2000', tipoDoc: 'CC' }}
      comprador={{ nombre: 'Ana Uno', documento: '1000', tipoDoc: 'CC', email: 'ana@example.com' }}
      archivosCount={0}
      identidadAprobada={false}
      firmaBaulPartes={[]}
      instanceId="inst-1"
      compradorBio={null}
      vendedorBio={null}
      resolverEdicion={resolverEdicion}
    />,
  );
}

describe('MatriculaResumen — botón Editar por sección', () => {
  it('sin resolvedor no pinta ningún «Editar» (resumen en solo lectura)', () => {
    renderResumen();
    expect(screen.queryByRole('button', { name: /^Editar/ })).toBeNull();
  });

  it('lleva al paso de cada sección y nombra el destino en el texto accesible', () => {
    const irVehiculo = vi.fn();
    const irComprador = vi.fn();
    renderResumen((seccion) => {
      if (seccion === 'vehiculo') return { pasoLabel: 'Consulta', ir: irVehiculo };
      if (seccion === 'comprador') return { pasoLabel: 'Actores', ir: irComprador };
      return null;
    });

    fireEvent.click(screen.getByRole('button', { name: 'Editar Vehículo: ir al paso «Consulta»' }));
    expect(irVehiculo).toHaveBeenCalledTimes(1);

    fireEvent.click(
      screen.getByRole('button', { name: /^Editar Comprador: ir al paso «Actores»/ }),
    );
    expect(irComprador).toHaveBeenCalledTimes(1);

    // Sin destino la tarjeta no ofrece un salto que no lleva a nada.
    expect(screen.queryByRole('button', { name: /^Editar Vendedor/ })).toBeNull();
  });
});

describe('ExpedienteVisor — botón Editar de «Documentos cargados»', () => {
  it('con destino lleva al paso de Requisitos; sin destino no se pinta', () => {
    const irRequisitos = vi.fn();
    const { rerender } = render(
      <ExpedienteVisor
        instanceId="inst-1"
        attachments={[]}
        editarDocumentos={{ pasoLabel: 'Requisitos', ir: irRequisitos }}
      />,
    );

    fireEvent.click(
      screen.getByRole('button', { name: 'Editar Documentos cargados: ir al paso «Requisitos»' }),
    );
    expect(irRequisitos).toHaveBeenCalledTimes(1);

    rerender(<ExpedienteVisor instanceId="inst-1" attachments={[]} />);
    expect(screen.queryByRole('button', { name: /^Editar Documentos/ })).toBeNull();
  });
});
