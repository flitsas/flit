import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { VehicleTransformationsCard } from '../VehicleTransformationsCard';
import type { FieldValue } from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    searchVehicleColors: vi.fn().mockResolvedValue([
      { id: '1', code: '1', name: 'NEGRO' },
      { id: '2', code: '2', name: 'BLANCO' },
      { id: '3', code: '3', name: 'ROJO' },
    ]),
    searchVehicleBodyworks: vi.fn().mockImplementation(async (vehicleClass?: string) => {
      const c = (vehicleClass ?? '').trim().toUpperCase();
      if (!c) {
        return [{ id: 'u1', code: '819', name: 'SIN CARROCERIA', classVehicle: null }];
      }
      if (c.includes('AUTOMOVIL')) {
        return [
          { id: 'a1', code: '19', name: 'COUPE', classVehicle: 'AUTOMOVIL' },
          { id: 'a2', code: '9', name: 'SEDAN', classVehicle: 'AUTOMOVIL' },
        ];
      }
      if (c.startsWith('CAMION') && !c.startsWith('CAMIONETA')) {
        return [
          { id: 'c1', code: '2', name: 'FURGON', classVehicle: 'CAMION' },
          { id: 'c2', code: '38', name: 'PLATAFORMA', classVehicle: 'CAMION' },
        ];
      }
      return [];
    }),
  },
}));

function fv(fieldKey: string, valueText: string | null): FieldValue {
  return { formFieldId: '', fieldKey, valueText, valueJson: null, source: 'consultation' };
}

function renderCard(values: FieldValue[], onPatch = vi.fn().mockResolvedValue(undefined), readOnly = false) {
  render(
    <VehicleTransformationsCard
      fieldValues={values}
      readOnly={readOnly}
      saving={false}
      onPatch={onPatch}
    />,
  );
  return onPatch;
}

/** Activa un subtrámite con el check independiente. */
async function activar(user: ReturnType<typeof userEvent.setup>, optionLabel: string) {
  await user.click(screen.getByRole('switch', { name: optionLabel }));
}

// RUNT ya consultado (color plata, combustible gasolina), sin transformación declarada.
const runtBase: FieldValue[] = [
  fv('plate', 'ABC123'),
  fv('vehicle_color', 'PLATA'),
  fv('vehicle_color_runt', 'PLATA'),
  fv('vehicle_fuel', 'GASOLINA'),
  fv('vehicle_fuel_runt', 'GASOLINA'),
];

describe('VehicleTransformationsCard — tarjeta "Trámites Simultáneos"', () => {
  it('no renderiza antes de consultar el RUNT (sin datos de vehículo)', () => {
    const { container } = render(
      <VehicleTransformationsCard fieldValues={[]} readOnly={false} saving={false} onPatch={vi.fn()} />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('estado neutro: sin subtrámites y resumen "sin transformaciones"', () => {
    renderCard(runtBase);
    expect(
      screen.getByText('Trámites Simultáneos — Transformaciones del Vehículo'),
    ).toBeInTheDocument();
    expect(screen.getByText(/Sin transformaciones declaradas/)).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'Cambio de Color' })).toHaveAttribute(
      'aria-checked',
      'false',
    );
    expect(screen.getByRole('switch', { name: 'Conversiones de Combustible' })).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'Cambio de Carrocería' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Agregar trámite simultáneo')).not.toBeInTheDocument();
    expect(screen.queryByText('Inscribir Prenda')).not.toBeInTheDocument();
    expect(screen.queryByText('Blindaje')).not.toBeInTheDocument();
  });

  it('seleccionar "Cambio de Color" marca el flag y deja el valor vacío (obligatorio escoger)', async () => {
    const user = userEvent.setup();
    const onPatch = renderCard(runtBase);

    await activar(user, 'Cambio de Color');

    expect(onPatch).toHaveBeenCalledWith([
      { fieldKey: 'cambio_color', valueText: 'true' },
      { fieldKey: 'vehicle_color', valueText: '' },
    ]);
  });

  it('con cambio_color activo sin valor nuevo, el select aparece vacío y obligatorio', () => {
    renderCard([...runtBase, fv('cambio_color', 'true')]);

    expect(screen.getByRole('switch', { name: 'Cambio de Color' })).toHaveAttribute(
      'aria-checked',
      'true',
    );
    expect(screen.getByText('* Obligatorio')).toBeInTheDocument();
    expect(screen.getByLabelText(/Nuevo color/)).toHaveTextContent('Selecciona…');
    expect(screen.getByText(/Escoge un valor distinto al del RUNT/)).toBeInTheDocument();
    expect(screen.queryByText(/Adjunta el soporte obligatorio/)).not.toBeInTheDocument();
    expect(screen.getByText(/PDF, JPG hasta 5MB · Opcional/)).toBeInTheDocument();
  });

  it('con cambio_color activo y valor nuevo, aparece la card con el valor', () => {
    renderCard([
      ...runtBase.filter((f) => f.fieldKey !== 'vehicle_color'),
      fv('vehicle_color', 'NEGRO'),
      fv('cambio_color', 'true'),
    ]);

    expect(screen.getByRole('switch', { name: 'Cambio de Color' })).toHaveAttribute(
      'aria-checked',
      'true',
    );
    expect(screen.getAllByText('Cambio de Color').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText(/Soporte de cambio de color/)).toBeInTheDocument();
    expect(screen.getByText('(soporte_cambio_color)')).toBeInTheDocument();
    expect(screen.getByLabelText(/Nuevo color/)).toHaveTextContent('NEGRO');
    expect(screen.getByText(/RUNT: PLATA/)).toBeInTheDocument();
    expect(screen.getByText(/Se registrará en el FUR — Color: NEGRO/)).toBeInTheDocument();
  });

  it('elegir un nuevo color desde el bloque de valor persiste el efectivo con el flag activo', async () => {
    const user = userEvent.setup();
    const onPatch = renderCard([
      ...runtBase,
      fv('cambio_color', 'true'),
    ]);

    await user.click(screen.getByLabelText(/Nuevo color/));
    await user.click(screen.getByRole('option', { name: 'NEGRO' }));

    expect(onPatch).toHaveBeenCalledWith([
      { fieldKey: 'cambio_color', valueText: 'true' },
      { fieldKey: 'vehicle_color', valueText: 'NEGRO' },
    ]);
  });

  it('elegir un nuevo combustible desde el selector de valor persiste el efectivo con el flag activo', async () => {
    const user = userEvent.setup();
    const onPatch = renderCard([
      ...runtBase,
      fv('cambio_combustible', 'true'),
    ]);

    await user.click(screen.getByLabelText(/Nuevo combustible/));
    await user.click(screen.getByRole('option', { name: 'ELECTRICO' }));

    expect(onPatch).toHaveBeenCalledWith([
      { fieldKey: 'cambio_combustible', valueText: 'true' },
      { fieldKey: 'vehicle_fuel', valueText: 'ELECTRICO' },
    ]);
  });

  it('apagar el check baja el flag y restaura el efectivo al snapshot RUNT', async () => {
    const user = userEvent.setup();
    const onPatch = renderCard([
      ...runtBase.filter((f) => f.fieldKey !== 'vehicle_color'),
      fv('vehicle_color', 'NEGRO'),
      fv('cambio_color', 'true'),
    ]);

    await user.click(screen.getByRole('switch', { name: 'Cambio de Color' }));

    expect(onPatch).toHaveBeenCalledWith([
      { fieldKey: 'cambio_color', valueText: 'false' },
      { fieldKey: 'vehicle_color', valueText: 'PLATA' },
    ]);
  });

  it('un borrador retomado con banderas en true rehidrata los subtrámites ya seleccionados', () => {
    renderCard([
      ...runtBase.filter((f) => f.fieldKey !== 'vehicle_color' && f.fieldKey !== 'vehicle_fuel'),
      fv('vehicle_color', 'NEGRO'),
      fv('cambio_color', 'true'),
      fv('vehicle_fuel', 'ELECTRICO'),
      fv('cambio_combustible', 'true'),
    ]);

    expect(screen.getByRole('switch', { name: 'Cambio de Color' })).toHaveAttribute(
      'aria-checked',
      'true',
    );
    expect(screen.getByRole('switch', { name: 'Conversiones de Combustible' })).toHaveAttribute(
      'aria-checked',
      'true',
    );
    expect(screen.getByRole('switch', { name: 'Cambio de Carrocería' })).toHaveAttribute(
      'aria-checked',
      'false',
    );
    expect(screen.getByLabelText(/Nuevo color/)).toHaveTextContent('NEGRO');
    expect(screen.getByLabelText(/Nuevo combustible/)).toHaveTextContent('ELECTRICO');
  });

  it('con flag activo y valor igual al RUNT, el select queda vacío (pendiente de escoger)', async () => {
    const user = userEvent.setup();
    renderCard([
      fv('plate', 'ABC123'),
      fv('vehicle_color', 'FUCSIA'),
      fv('vehicle_color_runt', 'FUCSIA'),
      fv('cambio_color', 'true'),
    ]);

    expect(screen.getByLabelText(/Nuevo color/)).toHaveTextContent('Selecciona…');
    await user.click(screen.getByLabelText(/Nuevo color/));
    // El valor RUNT no se ofrece como "nuevo"; el catálogo mock trae NEGRO/BLANCO/ROJO.
    expect(await screen.findByRole('option', { name: 'NEGRO' })).toBeInTheDocument();
  });

  it('en modo readOnly: los checks están deshabilitados', () => {
    renderCard(
      [
        ...runtBase.filter((f) => f.fieldKey !== 'vehicle_color'),
        fv('vehicle_color', 'NEGRO'),
        fv('cambio_color', 'true'),
      ],
      vi.fn(),
      true,
    );
    expect(screen.getByRole('switch', { name: 'Cambio de Color' })).toBeDisabled();
    expect(screen.getByRole('switch', { name: 'Conversiones de Combustible' })).toBeDisabled();
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

  it('seleccionar "Cambio de Carrocería" dispara onPatch con el flag correcto', async () => {
    const user = userEvent.setup();
    const onPatch = vi.fn().mockResolvedValue(undefined);
    render(
      <VehicleTransformationsCard fieldValues={runtConClase} readOnly={false} saving={false} onPatch={onPatch} />,
    );
    await activar(user, 'Cambio de Carrocería');
    expect(onPatch).toHaveBeenCalledWith(
      expect.arrayContaining([{ fieldKey: 'cambio_carroceria', valueText: 'true' }]),
    );
  });

  it('con cambio_carroceria activo y clase CAMION muestra el selector sin el valor RUNT', async () => {
    const user = userEvent.setup();
    render(
      <VehicleTransformationsCard
        fieldValues={[...runtConClase, fv('cambio_carroceria', 'true')]}
        readOnly={false}
        saving={false}
        onPatch={vi.fn()}
      />,
    );
    expect(screen.getByLabelText(/Nueva carrocería/)).toHaveTextContent('Selecciona…');
    await user.click(screen.getByLabelText(/Nueva carrocería/));
    // El valor actual del RUNT no se ofrece como “nuevo”.
    expect(screen.queryByRole('option', { name: 'ESTACAS' })).not.toBeInTheDocument();
    expect(screen.getAllByRole('option').length).toBeGreaterThan(0);
  });

  it('con clase AUTOMOVIL no lista carrocerías de otras clases', async () => {
    const user = userEvent.setup();
    render(
      <VehicleTransformationsCard
        fieldValues={[
          fv('plate', 'ABC123'),
          fv('vehicle_color', 'PLATA'),
          fv('vehicle_color_runt', 'PLATA'),
          fv('vehicle_fuel', 'GASOLINA'),
          fv('vehicle_fuel_runt', 'GASOLINA'),
          fv('vehicle_class', 'AUTOMOVIL'),
          fv('vehicle_body_type', 'SEDAN'),
          fv('vehicle_body_type_runt', 'SEDAN'),
          fv('cambio_carroceria', 'true'),
        ]}
        readOnly={false}
        saving={false}
        onPatch={vi.fn()}
      />,
    );
    await user.click(screen.getByLabelText(/Nueva carrocería/));
    expect(screen.getByRole('option', { name: 'COUPE' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'ESTACAS' })).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'FURGON' })).not.toBeInTheDocument();
  });

  it('muestra sin coincidencias si la clase es desconocida', async () => {
    const user = userEvent.setup();
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
    await user.click(screen.getByLabelText(/Nueva carrocería/));
    expect(await screen.findByText('Sin coincidencias')).toBeInTheDocument();
  });

  it('sin clase lista el respaldo (filas sin class_vehicle)', async () => {
    const user = userEvent.setup();
    const sinClase: FieldValue[] = [
      fv('plate', 'XYZ000'),
      fv('vehicle_color', 'AZUL'),
      fv('vehicle_color_runt', 'AZUL'),
      fv('vehicle_fuel', 'GASOLINA'),
      fv('vehicle_fuel_runt', 'GASOLINA'),
      fv('vehicle_body_type', 'ESTACAS'),
      fv('cambio_carroceria', 'true'),
    ];
    render(
      <VehicleTransformationsCard fieldValues={sinClase} readOnly={false} saving={false} onPatch={vi.fn()} />,
    );
    await user.click(screen.getByLabelText(/Nueva carrocería/));
    expect(await screen.findByRole('option', { name: 'SIN CARROCERIA' })).toBeInTheDocument();
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

/**
 * ADR-0050 — modo TIPO BASE (familia OTROS): la tarjeta deja de ser el acumulador del art. 5.1.8 y
 * pasa a capturar el único atributo que el trámite cambia por definición. Lo que desaparece es la
 * acumulación; la captura del valor nuevo y su soporte se conservan intactas, porque es justo lo que
 * el FUR tiene que imprimir.
 */
describe('VehicleTransformationsCard — modo tipo base (familia OTROS)', () => {
  it('no ofrece agregar otro trámite simultáneo', () => {
    render(
      <VehicleTransformationsCard
        fieldValues={runtBase}
        readOnly={false}
        saving={false}
        onPatch={vi.fn()}
        soloSubtramite="color"
      />,
    );

    expect(screen.queryByLabelText('Agregar trámite simultáneo')).not.toBeInTheDocument();
    expect(screen.queryByRole('switch', { name: 'Cambio de Color' })).not.toBeInTheDocument();
    expect(
      screen.queryByText('Trámites Simultáneos — Transformaciones del Vehículo'),
    ).not.toBeInTheDocument();
  });

  it('pinta el subtrámite del tipo YA activo, aunque no haya bandera ni cambio declarado', () => {
    // El gestor no lo activó: lo trajo el trámite. Esperar a la bandera dejaba la tarjeta vacía.
    render(
      <VehicleTransformationsCard
        fieldValues={runtBase}
        readOnly={false}
        saving={false}
        onPatch={vi.fn()}
        soloSubtramite="color"
      />,
    );

    expect(screen.getByText('Cambio de Color')).toBeInTheDocument();
    expect(screen.getByText(/Soporte de cambio de color/)).toBeInTheDocument();
    expect(screen.getByText('(soporte_cambio_color)')).toBeInTheDocument();
    expect(screen.getByText(/Escoge el nuevo color para declararlo en el FUR/)).toBeInTheDocument();
  });

  it('no deja quitar el cambio: quitarlo sería quedarse sin trámite', () => {
    render(
      <VehicleTransformationsCard
        fieldValues={runtBase}
        readOnly={false}
        saving={false}
        onPatch={vi.fn()}
        soloSubtramite="color"
      />,
    );

    expect(screen.queryByRole('button', { name: 'Quitar Cambio de Color' })).not.toBeInTheDocument();
  });

  it('no pinta los otros dos atributos aunque estén declarados en field_values', () => {
    // Residuo de un borrador anterior a la regla: el PATCH ya los rechaza, la pantalla no los repite.
    const conResiduo: FieldValue[] = [
      ...runtBase,
      fv('cambio_combustible', 'true'),
      fv('cambio_carroceria', 'true'),
    ];
    render(
      <VehicleTransformationsCard
        fieldValues={conResiduo}
        readOnly={false}
        saving={false}
        onPatch={vi.fn()}
        soloSubtramite="color"
      />,
    );

    expect(screen.getByText('Cambio de Color')).toBeInTheDocument();
    expect(screen.queryByText('Conversiones de Combustible')).not.toBeInTheDocument();
    expect(screen.queryByText('Cambio de Carrocería')).not.toBeInTheDocument();
  });

  it('sin modo tipo base, la tarjeta sigue ofreciendo las tres transformaciones (regresión)', () => {
    renderCard(runtBase);

    expect(screen.getByRole('switch', { name: 'Cambio de Color' })).toBeInTheDocument();
    expect(
      screen.getByText('Trámites Simultáneos — Transformaciones del Vehículo'),
    ).toBeInTheDocument();
  });
});
