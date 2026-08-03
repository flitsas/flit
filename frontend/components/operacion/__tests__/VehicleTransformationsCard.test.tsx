// A4/B4 (HU #10674 · ADR-0029) — tarjeta de transformaciones color/combustible/carrocería.
import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { VehicleTransformationsCard } from '../VehicleTransformationsCard';
import type { FieldValue } from '@/lib/api/types/procedure-runtime';

function fv(fieldKey: string, valueText: string | null): FieldValue {
  return { formFieldId: '', fieldKey, valueText, valueJson: null, source: 'consultation' };
}

function renderCard(values: FieldValue[], onPatch = vi.fn().mockResolvedValue(undefined)) {
  render(
    <VehicleTransformationsCard
      fieldValues={values}
      readOnly={false}
      saving={false}
      onPatch={onPatch}
    />,
  );
  return onPatch;
}

// RUNT ya consultado (color plata, combustible gasolina), sin transformación declarada.
const runtBase: FieldValue[] = [
  fv('plate', 'ABC123'),
  fv('vehicle_color', 'PLATA'),
  fv('vehicle_color_runt', 'PLATA'),
  fv('vehicle_fuel', 'GASOLINA'),
  fv('vehicle_fuel_runt', 'GASOLINA'),
];

describe('VehicleTransformationsCard', () => {
  it('no renderiza antes de consultar el RUNT (sin datos de vehículo)', () => {
    const { container } = render(
      <VehicleTransformationsCard fieldValues={[]} readOnly={false} saving={false} onPatch={vi.fn()} />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('estado neutro: muestra los valores RUNT y el resumen "sin transformaciones"', () => {
    renderCard(runtBase);
    expect(screen.getByText('Transformaciones del vehículo')).toBeInTheDocument();
    expect(screen.getByText(/RUNT: PLATA/)).toBeInTheDocument();
    expect(screen.getByText(/RUNT: GASOLINA/)).toBeInTheDocument();
    expect(screen.getByText(/Sin transformaciones declaradas/)).toBeInTheDocument();
    // Sin declarar cambio, no hay selector visible.
    expect(screen.queryByLabelText('Nuevo color')).not.toBeInTheDocument();
  });

  it('activar "Cambió el color" marca el flag y deja el efectivo en el valor RUNT', async () => {
    const user = userEvent.setup();
    const onPatch = renderCard(runtBase);

    await user.click(screen.getByLabelText('Cambió el color'));

    expect(onPatch).toHaveBeenCalledWith([
      { fieldKey: 'cambio_color', valueText: 'true' },
      { fieldKey: 'vehicle_color', valueText: 'PLATA' },
    ]);
  });

  it('con transformación de color activa muestra el selector y el diff RUNT → nuevo', () => {
    renderCard([
      ...runtBase.filter((f) => f.fieldKey !== 'vehicle_color'),
      fv('vehicle_color', 'NEGRO'),
      fv('cambio_color', 'true'),
    ]);

    const select = screen.getByLabelText('Nuevo color') as HTMLSelectElement;
    expect(select.value).toBe('NEGRO');
    // El resumen para el FUR muestra solo el valor NUEVO (sin flecha ni origen RUNT).
    expect(screen.getByText(/Se registrará en el FUR — Color: NEGRO/)).toBeInTheDocument();
    expect(screen.queryByText(/Se registrará en el FUR — Color: PLATA/)).not.toBeInTheDocument();
  });

  it('elegir un nuevo combustible persiste el efectivo con el flag activo', async () => {
    const user = userEvent.setup();
    const onPatch = renderCard([
      ...runtBase,
      fv('cambio_combustible', 'true'),
    ]);

    await user.selectOptions(screen.getByLabelText('Nuevo combustible'), 'ELECTRICO');

    expect(onPatch).toHaveBeenCalledWith([
      { fieldKey: 'cambio_combustible', valueText: 'true' },
      { fieldKey: 'vehicle_fuel', valueText: 'ELECTRICO' },
    ]);
  });

  it('desactivar el cambio restaura el efectivo al snapshot RUNT y baja el flag', async () => {
    const user = userEvent.setup();
    const onPatch = renderCard([
      ...runtBase.filter((f) => f.fieldKey !== 'vehicle_color'),
      fv('vehicle_color', 'NEGRO'),
      fv('cambio_color', 'true'),
    ]);

    await user.click(screen.getByLabelText('Cambió el color'));

    expect(onPatch).toHaveBeenCalledWith([
      { fieldKey: 'cambio_color', valueText: 'false' },
      { fieldKey: 'vehicle_color', valueText: 'PLATA' },
    ]);
  });

  it('un color RUNT fuera del catálogo placeholder sigue siendo opción válida del selector', () => {
    renderCard([
      fv('plate', 'ABC123'),
      fv('vehicle_color', 'FUCSIA'),
      fv('vehicle_color_runt', 'FUCSIA'),
      fv('cambio_color', 'true'),
    ]);

    const select = screen.getByLabelText('Nuevo color') as HTMLSelectElement;
    expect(select.value).toBe('FUCSIA');
    expect(within(select).getByRole('option', { name: 'FUCSIA' })).toBeInTheDocument();
  });

  it('en modo readOnly los controles quedan deshabilitados', () => {
    render(
      <VehicleTransformationsCard fieldValues={runtBase} readOnly saving={false} onPatch={vi.fn()} />,
    );
    expect(screen.getByLabelText('Cambió el color')).toBeDisabled();
    expect(screen.getByLabelText('Cambió el combustible')).toBeDisabled();
    expect(screen.getByLabelText('Cambió la carrocería')).toBeDisabled();
  });
});

describe('VehicleTransformationsCard — carrocería (P2/P3)', () => {
  const runtConClase: FieldValue[] = [
    fv('plate', 'ABC123'),
    fv('vehicle_color', 'PLATA'),
    fv('vehicle_color_runt', 'PLATA'),
    fv('vehicle_fuel', 'GASOLINA'),
    fv('vehicle_fuel_runt', 'GASOLINA'),
    fv('vehicle_class', 'CAMION'),
    fv('vehicle_body_type', 'ESTACAS'),
    fv('vehicle_body_type_runt', 'ESTACAS'),
  ];

  it('muestra la fila Cambió la carrocería cuando hay datos del vehículo', () => {
    render(
      <VehicleTransformationsCard fieldValues={runtConClase} readOnly={false} saving={false} onPatch={vi.fn()} />,
    );
    expect(screen.getByLabelText('Cambió la carrocería')).toBeInTheDocument();
    expect(screen.getByText(/RUNT: ESTACAS/)).toBeInTheDocument();
  });

  it('al activar carrocería dispara onPatch con el flag correcto', async () => {
    const user = userEvent.setup();
    const onPatch = vi.fn().mockResolvedValue(undefined);
    render(
      <VehicleTransformationsCard fieldValues={runtConClase} readOnly={false} saving={false} onPatch={onPatch} />,
    );
    await user.click(screen.getByLabelText('Cambió la carrocería'));
    expect(onPatch).toHaveBeenCalledWith(
      expect.arrayContaining([{ fieldKey: 'cambio_carroceria', valueText: 'true' }]),
    );
  });

  it('con cambio_carroceria activo y clase CAMION muestra selector filtrado', () => {
    render(
      <VehicleTransformationsCard
        fieldValues={[...runtConClase, fv('cambio_carroceria', 'true')]}
        readOnly={false}
        saving={false}
        onPatch={vi.fn()}
      />,
    );
    // El selector de carrocería es visible y tiene opciones de la clase CAMION
    const select = screen.getByLabelText('Nueva carrocería') as HTMLSelectElement;
    expect(select).toBeInTheDocument();
    expect(within(select).getByRole('option', { name: 'ESTACAS' })).toBeInTheDocument();
  });

  it('muestra mensaje si la clase es desconocida y no hay opciones', () => {
    const valoresClaseDesconocida: FieldValue[] = [
      fv('plate', 'XYZ999'),
      fv('vehicle_color', 'ROJO'),
      fv('vehicle_color_runt', 'ROJO'),
      fv('vehicle_fuel', 'GASOLINA'),
      fv('vehicle_fuel_runt', 'GASOLINA'),
      fv('vehicle_class', 'CLASE_INEXISTENTE_XYZ'),
      fv('vehicle_body_type', 'ESTACAS'),
      fv('vehicle_body_type_runt', 'ESTACAS'),
      fv('cambio_carroceria', 'true'),
    ];
    render(
      <VehicleTransformationsCard fieldValues={valoresClaseDesconocida} readOnly={false} saving={false} onPatch={vi.fn()} />,
    );
    expect(screen.getByText(/No hay carrocerías disponibles para la clase CLASE_INEXISTENTE_XYZ/)).toBeInTheDocument();
  });

  it('muestra mensaje de consulta RUNT cuando no hay clase', () => {
    const sinClase: FieldValue[] = [
      fv('plate', 'XYZ000'),
      fv('vehicle_color', 'AZUL'),
      fv('vehicle_color_runt', 'AZUL'),
      fv('vehicle_fuel', 'GASOLINA'),
      fv('vehicle_fuel_runt', 'GASOLINA'),
      // sin vehicle_class
      fv('vehicle_body_type', 'ESTACAS'),
      fv('cambio_carroceria', 'true'),
    ];
    render(
      <VehicleTransformationsCard fieldValues={sinClase} readOnly={false} saving={false} onPatch={vi.fn()} />,
    );
    expect(screen.getByText(/Consulta el RUNT para obtener la clase/)).toBeInTheDocument();
  });

  it('el resumen FUR incluye la carrocería cuando hay cambio declarado', () => {
    const values: FieldValue[] = [
      ...runtConClase.filter((f) => f.fieldKey !== 'vehicle_body_type'),
      fv('vehicle_body_type', 'FURGON'),
      fv('cambio_carroceria', 'true'),
    ];
    render(
      <VehicleTransformationsCard fieldValues={values} readOnly={false} saving={false} onPatch={vi.fn()} />,
    );
    expect(screen.getByText(/Carrocería: FURGON/)).toBeInTheDocument();
  });
});
