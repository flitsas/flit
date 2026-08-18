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

/** Agrega un subtrámite desde el selector "Agregar trámite simultáneo". */
async function agregar(user: ReturnType<typeof userEvent.setup>, optionLabel: string) {
  await user.selectOptions(screen.getByLabelText('Agregar trámite simultáneo'), optionLabel);
}

// RUNT ya consultado (color plata, combustible gasolina), sin transformación declarada.
const runtBase: FieldValue[] = [
  fv('plate', 'ABC123'),
  fv('vehicle_color', 'PLATA'),
  fv('vehicle_color_runt', 'PLATA'),
  fv('vehicle_fuel', 'GASOLINA'),
  fv('vehicle_fuel_runt', 'GASOLINA'),
];

describe('VehicleTransformationsCard — tarjeta "Trámites Simultáneos (Opcional)"', () => {
  it('no renderiza antes de consultar el RUNT (sin datos de vehículo)', () => {
    const { container } = render(
      <VehicleTransformationsCard fieldValues={[]} readOnly={false} saving={false} onPatch={vi.fn()} />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('estado neutro: sin subtrámites seleccionados y resumen "sin transformaciones"', () => {
    renderCard(runtBase);
    expect(screen.getByText('Trámites Simultáneos (Opcional)')).toBeInTheDocument();
    expect(screen.getByText('Sin trámites simultáneos seleccionados')).toBeInTheDocument();
    expect(screen.getByText(/Sin transformaciones declaradas/)).toBeInTheDocument();
    // El selector ofrece las tres opciones del catálogo, ninguna del catálogo de la propuesta que
    // FLIT excluye a propósito.
    const select = screen.getByLabelText('Agregar trámite simultáneo');
    expect(screen.getByRole('option', { name: 'Cambio de Color' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Conversiones de Combustible' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Cambio de Carrocería' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Inscribir Prenda' })).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Blindaje' })).not.toBeInTheDocument();
    expect(select).toBeInTheDocument();
  });

  it('seleccionar "Cambio de Color" lo agrega como chip, marca el flag y persiste el valor RUNT por defecto', async () => {
    const user = userEvent.setup();
    const onPatch = renderCard(runtBase);

    await agregar(user, 'Cambio de Color');

    expect(onPatch).toHaveBeenCalledWith([
      { fieldKey: 'cambio_color', valueText: 'true' },
      { fieldKey: 'vehicle_color', valueText: 'PLATA' },
    ]);
  });

  it('con cambio_color activo, el subtrámite aparece seleccionado con su chip y su bloque de valor', () => {
    renderCard([
      ...runtBase.filter((f) => f.fieldKey !== 'vehicle_color'),
      fv('vehicle_color', 'NEGRO'),
      fv('cambio_color', 'true'),
    ]);

    // Chip, identificado por su botón de quitar (el nombre también aparece en el bloque de valor).
    expect(screen.getByRole('button', { name: 'Quitar Cambio de Color' })).toBeInTheDocument();
    // Ya no está disponible para volver a agregarlo
    expect(screen.queryByRole('option', { name: 'Cambio de Color' })).not.toBeInTheDocument();
    // Bloque de valor con el campo del color efectivo
    expect(screen.getByLabelText('Nuevo color')).toHaveTextContent('NEGRO');
    expect(screen.getByText(/RUNT: PLATA/)).toBeInTheDocument();
    // El resumen para el FUR muestra solo el valor NUEVO (sin flecha ni origen RUNT).
    expect(screen.getByText(/Se registrará en el FUR — Color: NEGRO/)).toBeInTheDocument();
  });

  it('elegir un nuevo color desde el bloque de valor persiste el efectivo con el flag activo', async () => {
    const user = userEvent.setup();
    const onPatch = renderCard([
      ...runtBase,
      fv('cambio_color', 'true'),
    ]);

    await user.click(screen.getByLabelText('Nuevo color'));
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

    await user.click(screen.getByLabelText('Nuevo combustible'));
    await user.click(screen.getByRole('option', { name: 'ELECTRICO' }));

    expect(onPatch).toHaveBeenCalledWith([
      { fieldKey: 'cambio_combustible', valueText: 'true' },
      { fieldKey: 'vehicle_fuel', valueText: 'ELECTRICO' },
    ]);
  });

  it('quitar un subtrámite pide confirmación: cancelar no toca el flag ni el valor', async () => {
    const user = userEvent.setup();
    const onPatch = renderCard([
      ...runtBase.filter((f) => f.fieldKey !== 'vehicle_color'),
      fv('vehicle_color', 'NEGRO'),
      fv('cambio_color', 'true'),
    ]);

    await user.click(screen.getByRole('button', { name: 'Quitar Cambio de Color' }));
    expect(
      screen.getByText(/¿Estás seguro de eliminar «Cambio de Color»\?/),
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Cancelar' }));

    expect(onPatch).not.toHaveBeenCalled();
    // Sigue seleccionado: el diálogo se cerró sin aplicar el cambio.
    expect(screen.getByRole('button', { name: 'Quitar Cambio de Color' })).toBeInTheDocument();
  });

  it('quitar un subtrámite, al confirmar, baja el flag y restaura el efectivo al snapshot RUNT', async () => {
    const user = userEvent.setup();
    const onPatch = renderCard([
      ...runtBase.filter((f) => f.fieldKey !== 'vehicle_color'),
      fv('vehicle_color', 'NEGRO'),
      fv('cambio_color', 'true'),
    ]);

    await user.click(screen.getByRole('button', { name: 'Quitar Cambio de Color' }));
    await user.click(screen.getByRole('button', { name: 'Sí, eliminar' }));

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

    // Ambos chips presentes, cada uno con su valor ya cargado.
    expect(screen.getByRole('button', { name: 'Quitar Cambio de Color' })).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Quitar Conversiones de Combustible' }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Nuevo color')).toHaveTextContent('NEGRO');
    expect(screen.getByLabelText('Nuevo combustible')).toHaveTextContent('ELECTRICO');
    // Solo queda disponible el tercero.
    expect(screen.getByRole('option', { name: 'Cambio de Carrocería' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Cambio de Color' })).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Conversiones de Combustible' })).not.toBeInTheDocument();
  });

  it('un color RUNT fuera del catálogo API sigue siendo opción válida del selector de valor', async () => {
    const user = userEvent.setup();
    renderCard([
      fv('plate', 'ABC123'),
      fv('vehicle_color', 'FUCSIA'),
      fv('vehicle_color_runt', 'FUCSIA'),
      fv('cambio_color', 'true'),
    ]);

    expect(screen.getByLabelText('Nuevo color')).toHaveTextContent('FUCSIA');
    await user.click(screen.getByLabelText('Nuevo color'));
    expect(await screen.findByRole('option', { name: 'FUCSIA' })).toBeInTheDocument();
  });

  it('en modo readOnly: el selector de agregar está deshabilitado y los chips no ofrecen quitar', () => {
    renderCard(
      [
        ...runtBase.filter((f) => f.fieldKey !== 'vehicle_color'),
        fv('vehicle_color', 'NEGRO'),
        fv('cambio_color', 'true'),
      ],
      vi.fn(),
      true,
    );
    expect(screen.getByLabelText('Agregar trámite simultáneo')).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Quitar Cambio de Color' })).not.toBeInTheDocument();
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
    await agregar(user, 'Cambio de Carrocería');
    expect(onPatch).toHaveBeenCalledWith(
      expect.arrayContaining([{ fieldKey: 'cambio_carroceria', valueText: 'true' }]),
    );
  });

  it('con cambio_carroceria activo y clase CAMION muestra el selector de valor filtrado', async () => {
    const user = userEvent.setup();
    render(
      <VehicleTransformationsCard
        fieldValues={[...runtConClase, fv('cambio_carroceria', 'true')]}
        readOnly={false}
        saving={false}
        onPatch={vi.fn()}
      />,
    );
    expect(screen.getByLabelText('Nueva carrocería')).toBeInTheDocument();
    await user.click(screen.getByLabelText('Nueva carrocería'));
    expect(screen.getByRole('option', { name: 'ESTACAS' })).toBeInTheDocument();
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
