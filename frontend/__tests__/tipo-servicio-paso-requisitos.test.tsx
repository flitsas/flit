import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { FieldValue } from '@/lib/api/types/procedure-runtime';

/**
 * Captura del TIPO DE SERVICIO en el PASO DE REQUISITOS del wizard de trámites — solo matrícula
 * inicial, donde lo pone el repo de diseño (`MatriculaInicial`, Step 3). Antes vivía en el paso 1,
 * contra un trámite que todavía no existía; ahora el trámite ya está creado, así que cada elección
 * hace su PATCH inmediato y un borrador retomado vuelve a mostrar lo elegido.
 *
 * Seis valores del catálogo `vehicle-service-types`; con PUBLICO se exige además NIT + consulta
 * (directorio primero, RUES después) y la razón social de solo lectura. Hasta que eso esté, el
 * componente reporta el gate en `false` y el shell mantiene "Continuar" deshabilitado.
 * En traspaso no se ofrece el tipo de servicio (lo hidrata el RUNT), solo las transformaciones.
 */
const mocks = vi.hoisted(() => ({
  getInstance: vi.fn(),
  patchFieldValues: vi.fn(),
  listVehicleServiceTypes: vi.fn(),
  ruesPreview: vi.fn(),
  // Directorio de la compañía: se consulta ANTES que el RUES (misma escalera que el actor jurídico).
  lookupLegalRepresentativeByNit: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
  // Mismo duck-typing que la implementación real: `err.status === 503`.
  isRuesPreviewUnavailable: (err: unknown) =>
    !!err && typeof err === 'object' && (err as { status?: unknown }).status === 503,
}));

import { DeclaracionesTramite } from '@/components/operacion/DeclaracionesTramite';

const INSTANCE_ID = 'inst-1';
const NIT_EMPRESA = '900123456';

const TIPOS_SERVICIO = [
  { id: 'ts-1', code: 'PARTICULAR', name: 'Particular', sortOrder: 1 },
  { id: 'ts-2', code: 'PUBLICO', name: 'Público', sortOrder: 2 },
  { id: 'ts-3', code: 'DIPLOMATICO', name: 'Diplomático', sortOrder: 3 },
  { id: 'ts-4', code: 'OFICIAL', name: 'Oficial', sortOrder: 4 },
  { id: 'ts-5', code: 'ESPECIAL', name: 'Especial', sortOrder: 5 },
  { id: 'ts-6', code: 'OTROS', name: 'Otros', sortOrder: 6 },
];

const VEHICULO: FieldValue[] = [
  { formFieldId: '', fieldKey: 'vin', valueText: '9BWZZZ377VT004251', valueJson: null, source: 'consultation' },
  { formFieldId: '', fieldKey: 'vehicle_brand', valueText: 'RENAULT', valueJson: null, source: 'consultation' },
];

function renderDeclaraciones(
  modalidad: 'matricula_inicial' | 'traspaso' = 'matricula_inicial',
  onGate = vi.fn(),
) {
  render(
    <DeclaracionesTramite
      instanceId={INSTANCE_ID}
      modalidad={modalidad}
      onChanged={() => {}}
      onTipoServicioGateChange={onGate}
    />,
  );
  return onGate;
}

/** Último valor reportado al gate del pie ("Continuar"). */
function gateVigente(onGate: ReturnType<typeof vi.fn>): boolean | undefined {
  const last = onGate.mock.calls.at(-1);
  return last?.[0] as boolean | undefined;
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getInstance.mockResolvedValue({ id: INSTANCE_ID, fieldValues: VEHICULO });
  mocks.patchFieldValues.mockResolvedValue({ id: INSTANCE_ID, fieldValues: VEHICULO });
  mocks.listVehicleServiceTypes.mockResolvedValue(TIPOS_SERVICIO);
  // Sin coincidencia en el directorio: el flujo cae al RUES, que es lo que cubre la mayoría de estos casos.
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(null);
});

describe('Tipo de servicio — paso de requisitos (solo matrícula inicial)', () => {
  it('NO aparece en traspaso; los trámites simultáneos sí', async () => {
    renderDeclaraciones('traspaso');

    expect(
      await screen.findByRole('switch', { name: 'Cambio de Color' }),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText('Tipo de servicio')).not.toBeInTheDocument();
    expect(mocks.listVehicleServiceTypes).not.toHaveBeenCalled();
  });

  it('en matrícula ofrece las seis opciones del catálogo', async () => {
    renderDeclaraciones();

    const select = (await screen.findByLabelText('Tipo de servicio')) as HTMLSelectElement;
    await waitFor(() => expect(mocks.listVehicleServiceTypes).toHaveBeenCalledTimes(1));
    for (const tipo of TIPOS_SERVICIO) {
      expect(
        Array.from(select.options).some((o) => o.value === tipo.code && o.text === tipo.name),
      ).toBe(true);
    }
  });

  it('gatea "Continuar" mientras no haya tipo elegido', async () => {
    const onGate = renderDeclaraciones();

    await screen.findByLabelText('Tipo de servicio');
    await waitFor(() => expect(gateVigente(onGate)).toBe(false));
  });

  it('con un tipo distinto de PUBLICO, elegirlo basta: se guarda y se abre el paso', async () => {
    const user = userEvent.setup();
    const onGate = renderDeclaraciones();

    await user.selectOptions(await screen.findByLabelText('Tipo de servicio'), 'PARTICULAR');

    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith(
        INSTANCE_ID,
        expect.arrayContaining([
          expect.objectContaining({ fieldKey: 'vehicle_service', valueText: 'PARTICULAR' }),
        ]),
      ),
    );
    await waitFor(() => expect(gateVigente(onGate)).toBe(true));
    expect(screen.queryByLabelText('NIT empresa vinculadora')).not.toBeInTheDocument();
  });

  it('con PUBLICO sigue gateado hasta que la consulta devuelva la razón social', async () => {
    const user = userEvent.setup();
    const onGate = renderDeclaraciones();

    await user.selectOptions(await screen.findByLabelText('Tipo de servicio'), 'PUBLICO');

    expect(await screen.findByLabelText('NIT empresa vinculadora')).toBeInTheDocument();
    await waitFor(() => expect(gateVigente(onGate)).toBe(false));

    await user.type(screen.getByLabelText('NIT empresa vinculadora'), NIT_EMPRESA);
    // Sin consultar todavía: sigue gateado.
    expect(gateVigente(onGate)).toBe(false);
  });

  it('si la empresa está en el directorio, igual consulta RUES y usa esa razón social', async () => {
    mocks.lookupLegalRepresentativeByNit.mockResolvedValue({
      company: { nit: NIT_EMPRESA, razonSocial: 'BANCOLOMBIA S.A.S' },
      representatives: [],
    });
    mocks.ruesPreview.mockResolvedValue({ found: true, nit: NIT_EMPRESA, razonSocial: 'TRANSPORTES SAS' });
    const user = userEvent.setup();
    const onGate = renderDeclaraciones();

    await user.selectOptions(await screen.findByLabelText('Tipo de servicio'), 'PUBLICO');
    await user.type(await screen.findByLabelText('NIT empresa vinculadora'), NIT_EMPRESA);
    await user.click(screen.getByRole('button', { name: 'Buscar empresa en RUES' }));

    await waitFor(() => expect(mocks.ruesPreview).toHaveBeenCalled());
    const razonSocial = await screen.findByLabelText('Razón social');
    expect(razonSocial).toHaveTextContent(/^TRANSPORTES SAS$/);
    expect(screen.queryByText('Representante:')).toBeNull();
    await waitFor(() => expect(gateVigente(onGate)).toBe(true));
  });

  it('si el directorio falla, no bloquea: cae al RUES', async () => {
    mocks.lookupLegalRepresentativeByNit.mockRejectedValue(new Error('500'));
    mocks.ruesPreview.mockResolvedValue({ found: true, nit: NIT_EMPRESA, razonSocial: 'TRANSPORTES SAS' });
    const user = userEvent.setup();
    renderDeclaraciones();

    await user.selectOptions(await screen.findByLabelText('Tipo de servicio'), 'PUBLICO');
    await user.type(await screen.findByLabelText('NIT empresa vinculadora'), NIT_EMPRESA);
    await user.click(screen.getByRole('button', { name: 'Buscar empresa en RUES' }));

    await waitFor(() => expect(mocks.ruesPreview).toHaveBeenCalled());
    expect(await screen.findByLabelText('Razón social')).toHaveTextContent(/^TRANSPORTES SAS$/);
  });

  it('flujo feliz del RUES: la razón social llega de solo lectura y se guarda con el NIT', async () => {
    mocks.ruesPreview.mockResolvedValue({ found: true, nit: NIT_EMPRESA, razonSocial: 'TRANSPORTES SAS' });
    const user = userEvent.setup();
    const onGate = renderDeclaraciones();

    await user.selectOptions(await screen.findByLabelText('Tipo de servicio'), 'PUBLICO');
    await user.type(await screen.findByLabelText('NIT empresa vinculadora'), NIT_EMPRESA);
    await user.click(screen.getByRole('button', { name: 'Buscar empresa en RUES' }));

    await waitFor(() => expect(mocks.ruesPreview).toHaveBeenCalledWith({ documentNumber: NIT_EMPRESA }));
    const razonSocial = await screen.findByLabelText('Razón social');
    expect(razonSocial).toHaveTextContent(/^TRANSPORTES SAS$/);
    // No es un campo de formulario: es un `output` de solo lectura. La diferencia importa — un
    // `input` de una línea recorta las razones sociales largas del RUES y, al ser readOnly, el
    // resto del nombre queda fuera del alcance del gestor.
    expect(razonSocial.tagName).toBe('OUTPUT');

    // Casilla 19 del FUR: NIT y razón social se persisten juntos, no queda uno sin el otro.
    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith(
        INSTANCE_ID,
        expect.arrayContaining([
          expect.objectContaining({ fieldKey: 'empresa_vinculadora_nit', valueText: NIT_EMPRESA }),
          expect.objectContaining({
            fieldKey: 'empresa_vinculadora_razon_social',
            valueText: 'TRANSPORTES SAS',
          }),
        ]),
      ),
    );
    await waitFor(() => expect(gateVigente(onGate)).toBe(true));
  });

  it('RUES found:false — el proveedor respondió y el NIT no existe (no es un fallo transitorio)', async () => {
    mocks.ruesPreview.mockResolvedValue({ found: false, nit: NIT_EMPRESA, razonSocial: null });
    const user = userEvent.setup();
    const onGate = renderDeclaraciones();

    await user.selectOptions(await screen.findByLabelText('Tipo de servicio'), 'PUBLICO');
    await user.type(await screen.findByLabelText('NIT empresa vinculadora'), NIT_EMPRESA);
    await user.click(screen.getByRole('button', { name: 'Buscar empresa en RUES' }));

    expect(
      await screen.findByText(/No se encontró una empresa con ese NIT en el RUES/),
    ).toBeInTheDocument();
    // Distinto del mensaje de fallo transitorio (503): no se ofrece "Reintentar".
    expect(screen.queryByRole('button', { name: /Reintentar/i })).not.toBeInTheDocument();
    expect(gateVigente(onGate)).toBe(false);
  });

  /**
   * El recuadro de razón social NO existe hasta que se consulta: vacío no dice nada y se lee como un
   * campo pendiente de llenar, cuando no hay nada que el gestor pueda hacer ahí.
   */
  it('la razón social no se muestra antes de consultar', async () => {
    const user = userEvent.setup();
    renderDeclaraciones();

    await user.selectOptions(await screen.findByLabelText('Tipo de servicio'), 'PUBLICO');

    expect(screen.queryByLabelText('Razón social')).not.toBeInTheDocument();
  });

  /**
   * El RUES puede responder `found` SIN razón social. El placeholder se elegía con `??`, que no
   * cubre ese caso: el `output` quedaba vacío y el recuadro colapsaba a puro padding, sin decir por
   * qué. Los tres estados se distinguen: sin consultar, consultado con nombre, consultado sin nombre.
   */
  it('RUES found:true sin razón social — lo dice en vez de dejar el recuadro en blanco', async () => {
    mocks.ruesPreview.mockResolvedValue({ found: true, nit: NIT_EMPRESA, razonSocial: null });
    const user = userEvent.setup();
    renderDeclaraciones();

    await user.selectOptions(await screen.findByLabelText('Tipo de servicio'), 'PUBLICO');
    await user.type(await screen.findByLabelText('NIT empresa vinculadora'), NIT_EMPRESA);
    await user.click(screen.getByRole('button', { name: 'Buscar empresa en RUES' }));

    await waitFor(() => expect(mocks.ruesPreview).toHaveBeenCalled());
    const razonSocial = await screen.findByLabelText('Razón social');
    await waitFor(() =>
      expect(razonSocial).toHaveTextContent(/El RUES no reportó razón social para este NIT/),
    );
  });

  it('RUES 503 — fallo transitorio del proveedor, no del operador: se ofrece reintentar', async () => {
    mocks.ruesPreview.mockRejectedValueOnce({ status: 503, message: 'Service Unavailable' });
    mocks.ruesPreview.mockResolvedValueOnce({ found: true, nit: NIT_EMPRESA, razonSocial: 'TRANSPORTES SAS' });
    const user = userEvent.setup();
    const onGate = renderDeclaraciones();

    await user.selectOptions(await screen.findByLabelText('Tipo de servicio'), 'PUBLICO');
    await user.type(await screen.findByLabelText('NIT empresa vinculadora'), NIT_EMPRESA);
    await user.click(screen.getByRole('button', { name: 'Buscar empresa en RUES' }));

    expect(await screen.findByText(/El RUES no respondió/)).toBeInTheDocument();
    // Mensaje distinto del "no encontrado": aquí sí se ofrece reintentar.
    expect(screen.queryByText(/No se encontró una empresa con ese NIT/)).not.toBeInTheDocument();
    expect(gateVigente(onGate)).toBe(false);

    await user.click(screen.getByRole('button', { name: /Reintentar/i }));

    await waitFor(() => expect(mocks.ruesPreview).toHaveBeenCalledTimes(2));
    expect(await screen.findByLabelText('Razón social')).toHaveTextContent(/^TRANSPORTES SAS$/);
  });

  /**
   * Lo que el traslado al paso de requisitos hace posible y antes no lo era: reabrir un borrador y
   * ver (y corregir) lo elegido. En el paso 1 el tipo de servicio solo existía durante la creación.
   */
  it('un borrador retomado rehidrata el tipo de servicio y la empresa vinculadora', async () => {
    mocks.getInstance.mockResolvedValue({
      id: INSTANCE_ID,
      fieldValues: [
        ...VEHICULO,
        { formFieldId: '', fieldKey: 'vehicle_service', valueText: 'PUBLICO', valueJson: null, source: 'user' },
        { formFieldId: '', fieldKey: 'empresa_vinculadora_nit', valueText: NIT_EMPRESA, valueJson: null, source: 'user' },
        {
          formFieldId: '',
          fieldKey: 'empresa_vinculadora_razon_social',
          valueText: 'TRANSPORTES SAS',
          valueJson: null,
          source: 'user',
        },
      ],
    });
    const onGate = renderDeclaraciones();

    expect(await screen.findByLabelText('Tipo de servicio')).toHaveValue('PUBLICO');
    expect(screen.getByLabelText('NIT empresa vinculadora')).toHaveValue(NIT_EMPRESA);
    expect(screen.getByLabelText('Razón social')).toHaveTextContent(/^TRANSPORTES SAS$/);
    await waitFor(() => expect(gateVigente(onGate)).toBe(true));
  });

  /**
   * El RUNT también escribe `vehicle_service`, como texto libre ("PARTICULAR " con espacios, o un
   * valor que no está en el catálogo). Ese valor NO puede hidratar el selector: dejaría el campo en
   * blanco y el gate diciendo que ya está elegido.
   */
  it('un vehicle_service fuera del catálogo no cuenta como tipo elegido', async () => {
    mocks.getInstance.mockResolvedValue({
      id: INSTANCE_ID,
      fieldValues: [
        ...VEHICULO,
        { formFieldId: '', fieldKey: 'vehicle_service', valueText: 'Servicio no clasificado', valueJson: null, source: 'consultation' },
      ],
    });
    const onGate = renderDeclaraciones();

    expect(await screen.findByLabelText('Tipo de servicio')).toHaveValue('');
    await waitFor(() => expect(gateVigente(onGate)).toBe(false));
  });
});
