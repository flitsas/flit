// `FirmaFurStep` manda organismo + placa en un Fragment como `extrasSlot`. Deben quedar como celdas
// hermanas de la rejilla: en una sola celda, la tarjeta del organismo (`h-full`) tomaba toda la
// altura y la de placa se desbordaba encima de «Documentos cargados».
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import MatriculaResumen from '@/components/operacion/MatriculaResumen';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getInstance: vi.fn(() => Promise.resolve({ fieldValues: [] })),
    getActors: vi.fn(() => Promise.resolve([])),
    getBiometricState: vi.fn(() => Promise.resolve(null)),
    ensureIdentity: vi.fn(() => Promise.resolve({ outcome: 'requiere_validacion' })),
  },
}));

describe('MatriculaResumen — extrasSlot', () => {
  it('desenvuelve el Fragment: organismo y placa van en celdas distintas de la rejilla', () => {
    render(
      <MatriculaResumen
        modalidad="matricula_inicial"
        status="borrador"
        placa=""
        vehiculo="YAMAHA 2026"
        vin="VIN123"
        vendedor={null}
        comprador={{ nombre: 'Ana Uno', documento: '1000', tipoDoc: 'CC', email: 'ana@example.com' }}
        archivosCount={0}
        identidadAprobada={false}
        firmaBaulPartes={[]}
        instanceId="inst-1"
        compradorBio={null}
        vendedorBio={null}
        extrasSlot={
          <>
            <section aria-label="Organismo de tránsito">OT</section>
            <section aria-label="Placa">Sin placa</section>
          </>
        }
      />,
    );

    const organismoCell = screen.getByRole('region', { name: 'Organismo de tránsito' }).parentElement!;
    const placaCell = screen.getByRole('region', { name: 'Placa' }).parentElement!;
    expect(organismoCell).not.toBe(placaCell);
    expect(organismoCell.parentElement).toBe(placaCell.parentElement);
    // Par de tarjetas: ninguna ocupa las dos columnas.
    expect(placaCell.className ?? '').not.toContain('lg:col-span-2');
  });
});
