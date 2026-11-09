// HU 3 (registro diferido 2026-11-09) — el gestor ve si la placa que asignó el organismo de tránsito
// coincide con su dígito de preferencia. Tras la asignación el trámite ya no es borrador: la fila
// del listado abre el detalle (TramiteDetalleModal) y no el asistente, así que el aviso vive en la
// columna del vehículo de ese detalle, leyendo `fieldValues` del GET de la instancia.
//
// Uso de ejemplo:
//   <DetalleVehiculoSidebar item={item} canal={{
//     fieldValues: [{ fieldKey: 'plate', valueText: 'ABC125', source: 'ot', ... },
//                   { fieldKey: 'plate_preferred_last_digit', valueText: '5', ... }] }} />
import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { DetalleVehiculoSidebar } from '../DetalleVehiculoSidebar';
import type { FieldValue, InstanceSummary } from '@/lib/api/types/procedure-runtime';

const ITEM = {
  id: 'inst-1',
  referenceNumber: 'RAD-001',
  placa: 'ABC125',
  vin: 'VIN123',
  estado: 'asignado',
  modalidad: 'MATRICULA_INICIAL',
  vehiculoMarca: 'Renault',
  vehiculoLinea: 'Duster',
  createdAt: '2026-11-01T08:00:00Z',
  updatedAt: '2026-11-02T08:00:00Z',
} as unknown as InstanceSummary;

function fv(fieldKey: string, valueText: string, source = ''): FieldValue {
  return { formFieldId: `ff-${fieldKey}`, fieldKey, valueText, valueJson: null, source };
}

function renderSidebar(fieldValues: FieldValue[] | undefined, placaFila = 'ABC125') {
  return render(
    <DetalleVehiculoSidebar
      item={{ ...ITEM, placa: placaFila }}
      canal={{ fieldValues }}
    />,
  );
}

const COINCIDE =
  'La placa ABC125 asignada por el organismo de tránsito termina en 5, tu dígito de preferencia.';
const NO_COINCIDE =
  'La placa ABC123 asignada por el organismo de tránsito no termina en tu dígito de preferencia (5). El organismo lo toma solo como guía.';

describe('DetalleVehiculoSidebar — aviso del dígito de preferencia (HU 3)', () => {
  it('AC coincide — mensaje de éxito con el literal exacto', () => {
    renderSidebar([fv('plate', 'ABC125', 'ot'), fv('plate_preferred_last_digit', '5')]);
    const aviso = screen.getByTestId('detalle-aviso-digito-placa');
    expect(aviso).toHaveTextContent(COINCIDE);
    expect(aviso).toHaveAttribute('data-tono', 'exito');
    expect(aviso).toHaveAttribute('role', 'status');
  });

  it('AC no coincide — mensaje informativo (no de error) con el literal exacto', () => {
    renderSidebar([fv('plate', 'ABC123', 'ot'), fv('plate_preferred_last_digit', '5')], 'ABC123');
    const aviso = screen.getByTestId('detalle-aviso-digito-placa');
    expect(aviso).toHaveTextContent(NO_COINCIDE);
    expect(aviso).toHaveAttribute('data-tono', 'info');
    expect(aviso).not.toHaveAttribute('role', 'alert');
  });

  it('AC sin preferencia — vacía o "none": no se muestra aviso', () => {
    const { unmount } = renderSidebar([fv('plate', 'ABC125', 'ot'), fv('plate_preferred_last_digit', '')]);
    expect(screen.queryByTestId('detalle-aviso-digito-placa')).not.toBeInTheDocument();
    unmount();
    renderSidebar([fv('plate', 'ABC125', 'ot'), fv('plate_preferred_last_digit', 'none')]);
    expect(screen.queryByTestId('detalle-aviso-digito-placa')).not.toBeInTheDocument();
  });

  it('placa asignada antes del cambio (source vacío) también avisa', () => {
    renderSidebar([fv('plate', 'ABC125', ''), fv('plate_preferred_last_digit', '5')]);
    expect(screen.getByTestId('detalle-aviso-digito-placa')).toHaveTextContent(COINCIDE);
  });

  it('placa del RUNT (consultation) no es del OT: sin aviso', () => {
    renderSidebar([fv('plate', 'ABC125', 'consultation'), fv('plate_preferred_last_digit', '5')]);
    expect(screen.queryByTestId('detalle-aviso-digito-placa')).not.toBeInTheDocument();
  });

  it('detalle aún sin cargar (canal null) o sin fieldValues: sin aviso y sin lanzar', () => {
    render(<DetalleVehiculoSidebar item={ITEM} canal={null} />);
    expect(screen.queryByTestId('detalle-aviso-digito-placa')).not.toBeInTheDocument();
    expect(() => renderSidebar(undefined)).not.toThrow();
  });
});

// HU7 (corrección del usuario 2026-11-09) — en motos se compara el PRIMER dígito numérico.
describe('DetalleVehiculoSidebar — aviso del dígito en motos (HU7)', () => {
  it('moto ABC52D con preferencia 5 → literal de primer número', () => {
    renderSidebar(
      [fv('plate', 'ABC52D', 'ot'), fv('plate_preferred_last_digit', '5'), fv('vehicle_class', 'MOTOCICLETA')],
      'ABC52D',
    );
    expect(screen.getByTestId('detalle-aviso-digito-placa')).toHaveTextContent(
      'La placa ABC52D asignada por el organismo de tránsito tiene como primer número el 5, tu dígito de preferencia.',
    );
  });
});
