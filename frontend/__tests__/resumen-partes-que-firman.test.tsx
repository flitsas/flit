/**
 * ADR-0051 — el Resumen pinta el bloque de firma SOLO de las partes que el tipo somete a validación.
 *
 * El defecto: se deducía. El bloque del vendedor se condicionaba a `modalidad === 'traspaso'` y el
 * del comprador no se condicionaba a nada. En `TRASPASO_UNILATERAL` firma únicamente el propietario
 * (art. 5.3.2.2), así que el Resumen le pedía validación al locatario —persistido como `comprador`—
 * aunque el paso de Identidad ya no se la pidiera: las dos pantallas discrepaban sobre quién firma.
 */
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import MatriculaResumen from '@/components/operacion/MatriculaResumen';
import type { BiometricParte } from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    downloadBiometricCertificado: vi.fn(),
    downloadAttachment: vi.fn(),
    getBiometricState: vi.fn(() =>
      Promise.resolve({ validations: [], provider: 'mock', firmaBaulPartes: [] }),
    ),
    getFirmaPosterior: vi.fn(() => Promise.resolve({ aplica: false, marcado: false })),
    // `BiometricStep` (embebido en el resumen) los pide al montar.
    getActors: vi.fn(() => Promise.resolve([])),
    ensureIdentity: vi.fn(() => Promise.resolve({ outcome: 'requiere_validacion' })),
    getInstance: vi.fn(() => Promise.resolve({ fieldValues: [] })),
  },
}));

function renderResumen(partesBiometricas?: BiometricParte[]) {
  return render(
    <MatriculaResumen
      modalidad="traspaso"
      status="borrador"
      placa="ABC123"
      vehiculo="YAMAHA 2026"
      vin="VIN123"
      comprador={{ nombre: 'Daniel Amado', documento: '1193552679', tipoDoc: 'CC', email: 'd@e.com' }}
      vendedor={{ nombre: 'Willyn Londoño', documento: '1037669356', tipoDoc: 'CC', email: 'w@e.com' }}
      archivosCount={0}
      identidadAprobada={false}
      firmaBaulPartes={[]}
      instanceId="inst-1"
      compradorBio={null}
      vendedorBio={null}
      partesBiometricas={partesBiometricas}
    />,
  );
}

describe('MatriculaResumen — partes que firman', () => {
  it('en el unilateral solo pinta la firma del propietario', async () => {
    renderResumen(['vendedor']);

    expect(await screen.findByRole('group', { name: 'Biométrica Vendedor' })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Biométrica Comprador' })).toBeNull();
    // La parte que no firma lo dice, en vez de quedarse en blanco.
    expect(screen.getByText('No requiere firma')).toBeInTheDocument();
  });

  it('con las dos partes declaradas pinta las dos y ninguna dice que no firma', async () => {
    renderResumen(['vendedor', 'comprador']);

    expect(await screen.findByRole('group', { name: 'Biométrica Vendedor' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
    expect(screen.queryByText('No requiere firma')).toBeNull();
  });

  // Sin la prop —un borrador anterior a la capacidad, o cualquier consumidor que no la pase— manda el
  // criterio de siempre: en traspaso, las dos partes. Ningún tipo en operación cambia.
  it('sin partes declaradas conserva el criterio previo', async () => {
    renderResumen(undefined);

    expect(await screen.findByRole('group', { name: 'Biométrica Vendedor' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
  });
});

describe('MatriculaResumen — locatario y rótulos del catálogo', () => {
  it('pinta la tarjeta del locatario con sus datos y sin bloque de validación', () => {
    render(
      <MatriculaResumen
        modalidad="matricula_inicial"
        status="borrador"
        placa="ABC123"
        vehiculo="YAMAHA 2026"
        vin="VIN123"
        comprador={{ nombre: 'Leasing SAS', documento: '900123456', tipoDoc: 'NIT', email: 'l@e.com' }}
        vendedor={null}
        locatario={{
          nombre: 'Ana Arrendataria',
          documento: '52123456',
          tipoDoc: 'CC',
          email: 'ana@e.com',
          telefono: '3001112233',
          direccion: 'calle 5',
          ciudad: 'Medellín',
        }}
        archivosCount={0}
        identidadAprobada={false}
        firmaBaulPartes={[]}
        instanceId="inst-1"
        compradorBio={null}
        partesBiometricas={['comprador']}
      />,
    );

    // Sus datos salen: el resumen es el inventario del expediente.
    expect(screen.getByText('Ana Arrendataria')).toBeInTheDocument();
    expect(screen.getByText('ana@e.com')).toBeInTheDocument();
    // Pero no firma: ni captura biométrica ni certificado de identidad para él. El bloque de firma
    // del propietario, que sí firma, no se toca (lo cubre el resto del archivo).
    expect(screen.queryByRole('group', { name: /Biométrica Locatario/i })).toBeNull();
    expect(
      screen.queryByRole('button', { name: /Certificado ID · Locatario/i }),
    ).toBeNull();
    // Y en su lugar se dice por qué, en vez de dejar el hueco: las dos tarjetas de la fila igualan
    // altura, así que una que termina en los datos de contacto deja aire muerto al lado.
    expect(screen.getByText('No requiere firma')).toBeInTheDocument();
    expect(screen.getByText(/corresponden al propietario del vehículo/i)).toBeInTheDocument();
  });

  it('el rótulo del catálogo manda sobre el nombre del rol', () => {
    render(
      <MatriculaResumen
        modalidad="traspaso"
        status="borrador"
        placa="ABC123"
        vehiculo="YAMAHA 2026"
        vin="VIN123"
        comprador={{ nombre: 'Ana', documento: '52123456', tipoDoc: 'CC', email: 'a@e.com' }}
        vendedor={null}
        archivosCount={0}
        identidadAprobada={false}
        firmaBaulPartes={[]}
        instanceId="inst-1"
        compradorBio={null}
        partesBiometricas={['vendedor']}
        rotulosPorRol={{ comprador: 'Locatario' }}
      />,
    );

    expect(screen.getByText('Locatario')).toBeInTheDocument();
    expect(screen.queryByText('Comprador')).toBeNull();
  });
});

describe('MatriculaResumen — reparto de la rejilla', () => {
  const base = {
    status: 'borrador' as const,
    placa: 'ABC123',
    vehiculo: 'YAMAHA 2026',
    vin: 'VIN123',
    archivosCount: 0,
    identidadAprobada: false,
    firmaBaulPartes: [] as BiometricParte[],
    instanceId: 'inst-1',
    compradorBio: null,
  };
  const parte = (nombre: string) => ({
    nombre,
    documento: '52123456',
    tipoDoc: 'CC',
    email: `${nombre}@e.com`,
  });

  /**
   * La tarjeta del vehículo: ¿ocupa la fila entera?
   *
   * Se sube por el árbol comparando nombres de clase en vez de usar un selector CSS: `lg:col-span-2`
   * lleva dos puntos, que en un selector hay que escapar, y ese escape no sobrevive bien a las capas
   * de comillas.
   */
  function vehiculoAFilaCompleta(container: HTMLElement): boolean {
    const titulo = Array.from(container.querySelectorAll('*')).find(
      (el) => el.textContent?.trim() === 'Vehículo' && el.children.length === 0,
    );
    for (let el = titulo?.parentElement; el; el = el.parentElement) {
      if (el.className.includes('col-span-2')) return true;
      if (el === container) break;
    }
    return false;
  }

  it('con dos partes el vehículo se lleva la fila entera (leasing: propietario + locatario)', () => {
    const { container } = render(
      <MatriculaResumen
        {...base}
        modalidad="matricula_inicial"
        comprador={parte('Leasing')}
        vendedor={null}
        locatario={parte('Ana')}
      />,
    );

    expect(vehiculoAFilaCompleta(container)).toBe(true);
  });

  // La matrícula sin locatario no cambia: vehículo y comprador comparten la única fila.
  it('con una sola parte el vehículo comparte fila', () => {
    const { container } = render(
      <MatriculaResumen
        {...base}
        modalidad="matricula_inicial"
        comprador={parte('Ana')}
        vendedor={null}
      />,
    );

    expect(vehiculoAFilaCompleta(container)).toBe(false);
  });

  it('el traspaso conserva su reparto de siempre', () => {
    const { container } = render(
      <MatriculaResumen
        {...base}
        modalidad="traspaso"
        comprador={parte('Ana')}
        vendedor={parte('Willyn')}
      />,
    );

    expect(vehiculoAFilaCompleta(container)).toBe(true);
  });
});
