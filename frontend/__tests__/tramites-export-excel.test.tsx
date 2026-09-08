import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';
import {
  DEFAULT_TRAMITES_VISIBLE_COLUMNS,
  tramitesExportFields,
} from '@/lib/tramites/tramites-table-columns';
import { nombreArchivoTramites, selloDeArchivo } from '@/components/operacion/tramites-export';

/**
 * HU #12104 — descarga a Excel del listado de Trámites.
 *
 * Las pruebas de columnas van contra `tramitesExportFields` (pura) porque lo que hay que fijar es el
 * CONTRATO —qué datos salen y cuántas veces— y no el HTML que los rodea. El recorrido del universo
 * sí se prueba montando la tabla: lo que se quiere demostrar es que el archivo NO sale de las filas
 * ya pintadas, y eso solo se ve desde el componente.
 */

// ── Mocks ──────────────────────────────────────────────────────────
const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
  searchInstances: vi.fn(),
  searchEstadoCounts: vi.fn().mockResolvedValue({}),
  listFilterFields: vi.fn().mockResolvedValue([]),
  listInstanceEstadoCounts: vi.fn().mockResolvedValue({}),
  getConsultationConfig: vi.fn(),
  setPriority: vi.fn(),
  getAttachments: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  getInstance: vi.fn(),
  listBiometricExpediente: vi.fn(),
  pauseInstance: vi.fn(),
  pauseInstancesMassive: vi.fn(),
  completePlateFlow: vi.fn(),
  download: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

// Solo se sustituye `download`: `exportarPorLotes` y `buildWorkbook` corren de verdad, que es lo
// que permite afirmar sobre los BYTES del .xlsx en vez de sobre una promesa mockeada.
vi.mock('@/components/consultas/export', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/consultas/export')>()),
  download: mocks.download,
}));

vi.mock('@/lib/api/ui-preferences', () => ({
  uiPreferencesClient: {
    get: vi.fn().mockResolvedValue({ scope: 'tramites.columns', value: {} }),
    put: vi.fn().mockResolvedValue({ scope: 'tramites.columns', value: { visible: [] } }),
  },
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramitesTable } from '@/components/operacion/TramitesTable';

function makeInstance(i: number, extra: Partial<InstanceSummary> = {}): InstanceSummary {
  const num = String(i).padStart(4, '0');
  return {
    id: `inst-${num}`,
    referenceNumber: `TR-${num}`,
    modalidad: 'TRASPASO',
    estado: 'borrador',
    placa: `P${num}`,
    vin: `VIN-${num}`,
    vehiculoMarca: 'Toyota',
    vehiculoLinea: 'Corolla',
    compradorNombre: `Comprador ${num}`,
    compradorDocumento: `100${num}`,
    vendedorNombre: `Vendedor ${num}`,
    vendedorDocumento: `200${num}`,
    organismoTransito: 'SECRETARIA DE MOVILIDAD',
    pasoActual: 2,
    totalPasos: 6,
    createdAt: '2026-06-18T03:00:00Z',
    draftFinalizedAt: null,
    identityValidationStatus: null,
    signaturePending: false,
    canSubmit: false,
    prioritario: false,
    tenantId: '11111111-1111-1111-1111-111111111111',
    companiaNombre: null,
    subsanacionActiva: false,
    subsanacionCount: 0,
    ultimoRechazoMotivo: null,
    updatedAt: null,
    gestorNombre: null,
    fuente: 'dashboard',
    firmaVendedorEstado: 'pendiente',
    firmaCompradorEstado: 'pendiente',
    consolidadoAttachmentId: null,
    ...extra,
  } satisfies InstanceSummary;
}

/** El .xlsx es un zip STORE (sin comprimir): su XML aparece literal en los bytes. */
function xmlDe(bytes: Uint8Array): string {
  return new TextDecoder('latin1').decode(bytes);
}

/** Cuántas filas de DATOS lleva la hoja (la primera `<row` es el encabezado). */
function filasDe(bytes: Uint8Array): number {
  return (xmlDe(bytes).match(/<row /g) ?? []).length - 1;
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getConsultationConfig.mockResolvedValue({
    vehiclePlate: 'kyverum_runt',
    onlyOwnVehicles: false,
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  });
  mocks.listInstanceEstadoCounts.mockResolvedValue({});
  mocks.searchEstadoCounts.mockResolvedValue({});
  mocks.listFilterFields.mockResolvedValue([]);
  // El listado de la tabla usa el mismo camino POST que el export.
  if (!mocks.searchInstances.getMockImplementation()) {
    mocks.searchInstances.mockImplementation(async () => ({ items: [], total: 0 }));
  }
});

// ── AC3 — los datos apilados salen en columna propia ────────────────
describe('HU #12104 — AC3: las celdas compuestas se despliegan en columnas', () => {
  it('la vista por defecto exporta las fechas y el vehículo desglosados, sin repetir ninguno', () => {
    const ids = tramitesExportFields(DEFAULT_TRAMITES_VISIBLE_COLUMNS).map((c) => c.id);

    // Lo que la pantalla apila dentro de "Radicado" y "Vehículo" sale suelto en el archivo.
    expect(ids).toEqual([
      'radicado',
      'fechaCreacion',
      'fechaActualizacion',
      'placa',
      'vin',
      'vehiculo',
      'vendedor',
      'vendedorFirma',
      'comprador',
      'compradorFirma',
      'tramite',
      'estado',
      'paso',
      'pasoNombre',
      // HU #12183 — el .xlsx no puede llevar el ícono, así que las marcas salen en texto. Sin esta
      // columna el dato desaparecería justo en el archivo, que es donde nadie puede contrastarlo
      // con la pantalla.
      'marcas',
      'secretaria',
    ]);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('con la columna dedicada encendida el dato NO se duplica: sale una sola vez', () => {
    const ids = tramitesExportFields([...DEFAULT_TRAMITES_VISIBLE_COLUMNS, 'vin', 'fechaCreacion']).map(
      (c) => c.id,
    );

    expect(ids.filter((id) => id === 'vin')).toHaveLength(1);
    expect(ids.filter((id) => id === 'fechaCreacion')).toHaveLength(1);
    // Y sale por su columna dedicada, que va después en el orden de la tabla.
    expect(ids.indexOf('vin')).toBeGreaterThan(ids.indexOf('placa'));
  });

  it('una columna que el usuario apagó no aporta ningún dato al archivo', () => {
    const ids = tramitesExportFields(['radicado']).map((c) => c.id);
    expect(ids).toEqual(['radicado', 'fechaCreacion', 'fechaActualizacion']);
    expect(ids).not.toContain('placa');
  });
});

// ── AC4 — tipado real de Excel ──────────────────────────────────────
describe('HU #12104 — AC4: los valores llegan tipados', () => {
  const campos = tramitesExportFields(DEFAULT_TRAMITES_VISIBLE_COLUMNS);
  const campo = (id: string) => campos.find((c) => c.id === id)!;

  it('las fechas salen como fecha en reloj de Bogotá, no como texto', () => {
    // 03:00 UTC del 18 de junio son las 22:00 del 17 en Bogotá: sin el huso, el Excel diría 18 y
    // contradiría la pantalla.
    const raw = campo('fechaCreacion').raw(makeInstance(1));
    expect(raw).toEqual({ year: 2026, month: 6, day: 17 });
  });

  it('HU #12154 — el consecutivo va como TEXTO, no como número', () => {
    // Decisión consciente: como número, Excel le mete separador de miles y el 4571 se lee «4.571»,
    // que ya no parece un identificador. Como texto sale exacto. Si alguien lo «mejora» a numérico
    // para que ordene en Excel, esta prueba se lo dice.
    const conConsecutivo = makeInstance(1, { referenceNumber: '4571' });

    expect(campo('radicado').raw(conConsecutivo)).toBe('4571');
    expect(campo('radicado').value(conConsecutivo)).toBe('4571');
    expect(typeof campo('radicado').raw(conConsecutivo)).toBe('string');
  });

  it('una celda sin dato va VACÍA, nunca con un guion', () => {
    const sinVin = makeInstance(1, { vin: null, updatedAt: null });
    expect(campo('vin').raw(sinVin)).toBeNull();
    expect(campo('fechaActualizacion').raw(sinVin)).toBeNull();
    // En pantalla sí se pinta el guion: lo que cambia es lo que va al archivo.
    expect(campo('vin').value(sinVin)).toBe('—');
  });

  it('sin actor capturado no se reporta acreditación (misma regla que la celda)', () => {
    const sinVendedor = makeInstance(1, { vendedorNombre: null, firmaVendedorEstado: null });
    expect(campo('vendedorFirma').raw(sinVendedor)).toBeNull();
    // Con actor pero sin estado sí se dice, como en pantalla.
    expect(campo('vendedorFirma').raw(makeInstance(1, { firmaVendedorEstado: null }))).toBe(
      'Sin registrar',
    );
  });
});

// ── AC5 — nombre del archivo y reparto ──────────────────────────────
describe('HU #12104 — AC5: nombre del archivo', () => {
  it('lleva fecha y hora de generación', () => {
    expect(nombreArchivoTramites(selloDeArchivo(new Date(2026, 8, 7, 10, 30)))).toBe(
      'tramites_2026-09-07_10-30.xlsx',
    );
  });

  it('numera las partes solo cuando hay más de un archivo', () => {
    const sello = '2026-09-07_10-30';
    expect(nombreArchivoTramites(sello, { numero: 1, total: 1 })).toBe('tramites_2026-09-07_10-30.xlsx');
    expect(nombreArchivoTramites(sello, { numero: 2, total: 3 })).toBe(
      'tramites_2026-09-07_10-30_parte_2_de_3.xlsx',
    );
  });
});

// ── AC1/AC2/AC6/AC7 — el recorrido, desde la tabla ──────────────────
describe('HU #12104 — el archivo sale del universo, no de la página', () => {
  it('AC2: recorre TODAS las páginas del servidor aunque la tabla muestre 10 filas', async () => {
    const universo = Array.from({ length: 450 }, (_, i) => makeInstance(i + 1));
    mocks.listInstances.mockResolvedValue(universo.slice(0, 200));
    mocks.searchInstances.mockImplementation(({ skip = 0, take = 200 }) =>
      Promise.resolve({ items: universo.slice(skip, skip + take), total: universo.length }),
    );

    render(<TramitesTable />);
    await screen.findByText('TR-0001');

    await userEvent.click(screen.getByTestId('tramites-export-xlsx'));

    await waitFor(() => expect(mocks.download).toHaveBeenCalledTimes(1));
    // Un solo archivo (450 < 5.000) con las 450 filas, no con las 10 de la página.
    expect(filasDe(mocks.download.mock.calls[0][0])).toBe(450);
    expect(mocks.download.mock.calls[0][1]).toMatch(/^tramites_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}\.xlsx$/);
    // Tres páginas de 200 las pide el export (la primera, para conocer el total). La cuarta llamada
    // es la del propio listado al montar: desde la HU #12107 tabla y export comparten
    // `searchInstances`, que es justo lo que hace que un filtro nuevo llegue a los dos a la vez.
    expect(mocks.searchInstances).toHaveBeenCalledTimes(4);
    expect(screen.getByTestId('tramites-export-aviso')).toHaveTextContent('Se exportaron 450 trámites');
  });

  it('AC1: la búsqueda libre, que solo vive en cliente, también acota el archivo', async () => {
    const universo = [
      makeInstance(1, { placa: 'ABC123' }),
      makeInstance(2, { placa: 'XYZ999' }),
      makeInstance(3, { placa: 'ABC777' }),
    ];
    mocks.listInstances.mockResolvedValue(universo);
    mocks.searchInstances.mockImplementation(({ skip = 0, take = 200 }) =>
      Promise.resolve({ items: universo.slice(skip, skip + take), total: universo.length }),
    );

    render(<TramitesTable />);
    await screen.findByText('TR-0001');

    await userEvent.type(screen.getByPlaceholderText(/buscar/i), 'ABC');
    await userEvent.click(screen.getByTestId('tramites-export-xlsx'));

    await waitFor(() => expect(mocks.download).toHaveBeenCalledTimes(1));
    const xml = xmlDe(mocks.download.mock.calls[0][0]);
    expect(filasDe(mocks.download.mock.calls[0][0])).toBe(2);
    expect(xml).toContain('ABC123');
    expect(xml).not.toContain('XYZ999');
  });

  it('AC5: por encima de 5.000 filas reparte en varios archivos, en el mismo clic', async () => {
    // 5.200 filas = dos archivos (5.000 + 200). Se construyen con `take` de 200, como el servidor.
    const universo = Array.from({ length: 5200 }, (_, i) => makeInstance(i + 1));
    mocks.listInstances.mockResolvedValue(universo.slice(0, 200));
    mocks.searchInstances.mockImplementation(({ skip = 0, take = 200 }) =>
      Promise.resolve({ items: universo.slice(skip, skip + take), total: universo.length }),
    );

    render(<TramitesTable />);
    await screen.findByText('TR-0001');

    await userEvent.click(screen.getByTestId('tramites-export-xlsx'));

    await waitFor(() => expect(mocks.download).toHaveBeenCalledTimes(2), { timeout: 30_000 });

    const [primero, segundo] = mocks.download.mock.calls;
    expect(primero[1]).toMatch(/_parte_1_de_2\.xlsx$/);
    expect(segundo[1]).toMatch(/_parte_2_de_2\.xlsx$/);
    // Ninguna fila se pierde ni se repite en el corte entre archivos.
    expect(filasDe(primero[0])).toBe(5000);
    expect(filasDe(segundo[0])).toBe(200);
    expect(xmlDe(primero[0])).toContain('TR-5000');
    expect(xmlDe(segundo[0])).toContain('TR-5001');
    expect(screen.getByTestId('tramites-export-aviso')).toHaveTextContent(
      'Se exportaron 5200 trámites en 2 archivos',
    );
  }, 40_000);

  it('AC6: sin ningún trámite que cumpla los filtros no se descarga archivo, y se dice', async () => {
    mocks.listInstances.mockResolvedValue([]);
    mocks.searchInstances.mockResolvedValue({ items: [], total: 0 });

    render(<TramitesTable />);
    await userEvent.click(await screen.findByTestId('tramites-export-xlsx'));

    await waitFor(() =>
      expect(screen.getByTestId('tramites-export-aviso')).toHaveTextContent(
        /Ningún trámite cumple los filtros/i,
      ),
    );
    expect(mocks.download).not.toHaveBeenCalled();
  });

  it('AC7: si una página falla se avisa y NO se deja un archivo incompleto', async () => {
    const universo = Array.from({ length: 300 }, (_, i) => makeInstance(i + 1));
    mocks.listInstances.mockResolvedValue(universo.slice(0, 200));
    mocks.searchInstances
      .mockResolvedValueOnce({ items: universo.slice(0, 200), total: 300 })
      .mockRejectedValueOnce(new Error('502 Bad Gateway'));

    render(<TramitesTable />);
    await screen.findByText('TR-0001');

    await userEvent.click(screen.getByTestId('tramites-export-xlsx'));

    expect(await screen.findByText(/502 Bad Gateway/i)).toBeInTheDocument();
    expect(mocks.download).not.toHaveBeenCalled();
    // La tabla sigue en pie con sus filas.
    expect(screen.getByText('TR-0001')).toBeInTheDocument();
  });
});
