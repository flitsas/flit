import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { TramiteDetalleVehiculo } from '../TramiteDetalleVehiculo';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  FieldValue,
  InstanceSummary,
  PreflightSnapshot,
  ProcedureInstanceDetail,
} from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getInstance: vi.fn(),
    getPreflight: vi.fn(),
  },
}));

const client = vi.mocked(tramitesClient);

const ITEM = { id: 'inst-1', modalidad: 'matricula_inicial' } as unknown as InstanceSummary;

function fv(fieldKey: string, valueText: string | null): FieldValue {
  return { formFieldId: '', fieldKey, valueText, valueJson: null, source: 'consultation' };
}

function detail(fieldValues: FieldValue[]): ProcedureInstanceDetail {
  return { fieldValues } as unknown as ProcedureInstanceDetail;
}

function renderSeccion() {
  return render(<TramiteDetalleVehiculo instanceId="inst-1" item={ITEM} />);
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('TramiteDetalleVehiculo — estado cargando', () => {
  it('muestra el esqueleto de carga en las dos tarjetas mientras las llamadas están pendientes', () => {
    client.getInstance.mockReturnValue(new Promise(() => {}));
    client.getPreflight.mockReturnValue(new Promise(() => {}));

    renderSeccion();

    expect(screen.getByLabelText('Cargando especificaciones técnicas')).toBeInTheDocument();
    expect(screen.getByLabelText('Cargando verificación de requisitos')).toBeInTheDocument();
  });
});

describe('TramiteDetalleVehiculo — solo pinta las claves de fieldValues que existen', () => {
  it('omite las especificaciones cuyo fieldKey no llegó, en vez de dejarlas en "—"', async () => {
    client.getInstance.mockResolvedValue(
      detail([fv('vehicle_class', 'Automóvil'), fv('vehicle_fuel', 'Gasolina')]),
    );
    client.getPreflight.mockResolvedValue(null);

    renderSeccion();

    expect(await screen.findByText('Clase')).toBeInTheDocument();
    expect(screen.getByText('Automóvil')).toBeInTheDocument();
    expect(screen.getByText('Combustible')).toBeInTheDocument();
    expect(screen.getByText('Gasolina')).toBeInTheDocument();

    // Ausentes en fieldValues: no se inventan ni se pintan con "—".
    expect(screen.queryByText('Servicio')).not.toBeInTheDocument();
    expect(screen.queryByText('Cilindraje')).not.toBeInTheDocument();
    expect(screen.queryByText('Carrocería')).not.toBeInTheDocument();
    expect(screen.queryByText('Capacidad')).not.toBeInTheDocument();
    expect(screen.queryByText('Ejes')).not.toBeInTheDocument();
    expect(screen.queryByText('Alto')).not.toBeInTheDocument();
    expect(screen.queryByText('Largo')).not.toBeInTheDocument();
    expect(screen.queryByText('Estado')).not.toBeInTheDocument();
    expect(screen.queryByText('N. Motor')).not.toBeInTheDocument();
    expect(screen.queryByText('N. Chasis')).not.toBeInTheDocument();
    expect(screen.queryByText('N. Serie')).not.toBeInTheDocument();
    expect(screen.queryByText('Empresa vinculadora')).not.toBeInTheDocument();
    expect(screen.queryByText('—')).not.toBeInTheDocument();
  });

  it('usa el estado vacío cuando ninguna clave conocida llegó en fieldValues', async () => {
    client.getInstance.mockResolvedValue(detail([fv('otro_campo_no_mapeado', 'x')]));
    client.getPreflight.mockResolvedValue(null);

    renderSeccion();

    expect(
      await screen.findByText('Este trámite no tiene especificaciones técnicas del vehículo registradas todavía.'),
    ).toBeInTheDocument();
  });
});

describe('TramiteDetalleVehiculo — trámite sin verificación ejecutada', () => {
  it('usa el estado vacío en vez de un error cuando getPreflight resuelve null', async () => {
    client.getInstance.mockResolvedValue(detail([fv('vehicle_class', 'Automóvil')]));
    client.getPreflight.mockResolvedValue(null);

    renderSeccion();

    expect(
      await screen.findByText(
        'Este trámite no tiene una verificación de requisitos ejecutada (RUNT/SIMIT/RNMC). Se ejecuta desde el asistente, no desde este detalle.',
      ),
    ).toBeInTheDocument();
    // No dispara ninguna consulta: no hay botón de acción en esta sección de solo lectura.
    expect(screen.queryByRole('button', { name: /consultar/i })).not.toBeInTheDocument();
  });
});

describe('TramiteDetalleVehiculo — fallo de una sola de las dos fuentes', () => {
  it('si falla getInstance, la verificación igual se pinta (y el error queda solo en su tarjeta)', async () => {
    client.getInstance.mockRejectedValue(new Error('El servicio no está disponible en este momento.'));
    const snapshot: PreflightSnapshot = {
      overall: 'green',
      checks: [{ key: 'runt', label: 'RUNT', status: 'ok', source: 'verifik', message: '' }],
      createdAt: '2026-05-17T10:38:00Z',
    };
    client.getPreflight.mockResolvedValue(snapshot);

    renderSeccion();

    expect(
      await screen.findByText('El servicio no está disponible en este momento.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
    // La verificación sí llegó y se pinta normal.
    expect(await screen.findByText('RUNT')).toBeInTheDocument();
    expect(screen.getByLabelText('RUNT: OK')).toBeInTheDocument();
  });

  it('si falla getPreflight, las especificaciones igual se pintan (y el error queda solo en su tarjeta)', async () => {
    client.getInstance.mockResolvedValue(detail([fv('vehicle_class', 'Automóvil')]));
    client.getPreflight.mockRejectedValue(new Error('Tiempo de espera agotado.'));

    renderSeccion();

    expect(await screen.findByText('Automóvil')).toBeInTheDocument();
    expect(await screen.findByText('Tiempo de espera agotado.')).toBeInTheDocument();
  });

  it('ofrece reintentar y solo repite la llamada de la tarjeta que falló', async () => {
    client.getInstance.mockResolvedValue(detail([fv('vehicle_class', 'Automóvil')]));
    client.getPreflight.mockRejectedValueOnce(new Error('fallo de red'));
    client.getPreflight.mockResolvedValueOnce({
      overall: 'green',
      checks: [{ key: 'runt', label: 'RUNT', status: 'ok', source: 'verifik', message: '' }],
      createdAt: '2026-05-17T10:38:00Z',
    });

    renderSeccion();

    const boton = await screen.findByRole('button', { name: 'Reintentar' });
    boton.click();

    expect(await screen.findByText('RUNT')).toBeInTheDocument();
    expect(client.getInstance).toHaveBeenCalledTimes(1);
    expect(client.getPreflight).toHaveBeenCalledTimes(2);
  });
});

describe('TramiteDetalleVehiculo — estado lleno', () => {
  it('pinta las especificaciones (incluida la empresa vinculadora con servicio Público) y el semáforo de la verificación', async () => {
    client.getInstance.mockResolvedValue(
      detail([
        fv('vehicle_class', 'Automóvil'),
        fv('vehicle_service', 'PUBLICO'),
        fv('empresa_vinculadora_razon_social', 'Transportes ABC S.A.S.'),
        fv('empresa_vinculadora_nit', '900123456-1'),
        fv('vehicle_engine_displacement', '1998'),
        fv('vehicle_fuel', 'Gasolina'),
        fv('vehicle_body_type', 'Wagon'),
      ]),
    );
    const largoMensaje =
      'No fue posible verificar la revisión tecnomecánica con el proveedor en este momento, vuelve a intentarlo más tarde.';
    const snapshot: PreflightSnapshot = {
      overall: 'yellow',
      checks: [
        { key: 'runt', label: 'RUNT', status: 'ok', source: 'verifik', message: '' },
        { key: 'soat', label: 'SOAT', status: 'warn', source: 'verifik', message: 'Vence en 7 días' },
        { key: 'tecno', label: 'Tecnomecánica', status: 'fail', source: 'verifik', message: largoMensaje },
      ],
      createdAt: '2026-05-17T10:38:00Z',
    };
    client.getPreflight.mockResolvedValue(snapshot);

    renderSeccion();

    expect(await screen.findByText('Automóvil')).toBeInTheDocument();
    expect(screen.getByText('PUBLICO')).toBeInTheDocument();
    expect(screen.getByText('Empresa vinculadora')).toBeInTheDocument();
    expect(screen.getByText('Transportes ABC S.A.S.')).toBeInTheDocument();
    expect(screen.getByText('NIT empresa vinculadora')).toBeInTheDocument();
    expect(screen.getByText('900123456-1')).toBeInTheDocument();
    // Cilindraje sin sufijo en el dato crudo: se le agrega "cc".
    expect(screen.getByText('1998 cc')).toBeInTheDocument();

    // Resumen del semáforo arriba de la tarjeta.
    expect(screen.getByText('Con advertencias')).toBeInTheDocument();

    // RUNT: OK.
    expect(screen.getByLabelText('RUNT: OK')).toBeInTheDocument();

    // SOAT: mensaje corto, en línea junto a la etiqueta (mismo renglón, sin párrafo aparte).
    const soatBadge = screen.getByLabelText('SOAT: ADVERTENCIA');
    const soatRow = soatBadge.closest('li')!;
    expect(within(soatRow).getByText(/Vence en 7 días/)).toBeInTheDocument();
    expect(soatRow.querySelector('p')).toBeNull();

    // Tecnomecánica: mensaje largo, en su propio párrafo bajo el renglón.
    const tecnoBadge = screen.getByLabelText('Tecnomecánica: FALLA');
    const tecnoRow = tecnoBadge.closest('li')!;
    expect(tecnoRow.querySelector('p')).not.toBeNull();
    expect(within(tecnoRow).getByText(largoMensaje)).toBeInTheDocument();

    // El estado nunca depende solo del color: cada badge trae su palabra visible.
    expect(screen.getByText('OK')).toBeInTheDocument();
    expect(screen.getByText('ADVERTENCIA')).toBeInTheDocument();
    expect(screen.getByText('FALLA')).toBeInTheDocument();
  });

  it('pinta alto, largo, ancho y llantas cuando llegaron en fieldValues', async () => {
    client.getInstance.mockResolvedValue(
      detail([
        fv('vehicle_axles', '3'),
        fv('vehicle_height', '2000'),
        fv('vehicle_width', '2980'),
        fv('vehicle_length', '15500'),
        fv('vehicle_tires', '12'),
      ]),
    );
    client.getPreflight.mockResolvedValue(null);

    renderSeccion();

    expect(await screen.findByText('Ejes')).toBeInTheDocument();
    expect(screen.getByText('3')).toBeInTheDocument();
    expect(screen.getByText('Alto')).toBeInTheDocument();
    expect(screen.getByText('2000 mm')).toBeInTheDocument();
    expect(screen.getByText('Ancho')).toBeInTheDocument();
    expect(screen.getByText('2980 mm')).toBeInTheDocument();
    expect(screen.getByText('Largo')).toBeInTheDocument();
    expect(screen.getByText('15500 mm')).toBeInTheDocument();
    expect(screen.getByText('Llantas')).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument();
  });
});

describe('TramiteDetalleVehiculo — cancelación al desmontar', () => {
  it('no actualiza el estado si el componente se desmonta antes de que resuelvan las llamadas', async () => {
    let resolverInstance: (v: ProcedureInstanceDetail) => void = () => {};
    client.getInstance.mockReturnValue(
      new Promise((resolve) => {
        resolverInstance = resolve;
      }),
    );
    client.getPreflight.mockResolvedValue(null);

    const { unmount } = renderSeccion();
    unmount();

    expect(() => resolverInstance(detail([fv('vehicle_class', 'Automóvil')]))).not.toThrow();
  });
});
