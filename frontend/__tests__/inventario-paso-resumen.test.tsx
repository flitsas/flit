import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type {
  BiometricValidation,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

/**
 * INVENTARIO DEL PASO DE RESUMEN (`FirmaFurStep` + `MatriculaResumen`).
 *
 * Red de seguridad del rediseño visual: fija QUÉ tiene que seguir mostrando el último paso, no cómo
 * se ve. Es la única pantalla donde el gestor revisa de una sentada TODO lo que va a radicar —el
 * vehículo con sus especificaciones, las partes, el mandatario, las transformaciones declaradas, la
 * prenda y los documentos generados—, así que aquí un dato que desaparece no es una molestia: es un
 * error que se radica ante el organismo. El resumen no captura casi nada; su trabajo es no callarse
 * nada.
 *
 * Se consulta por rol accesible (`region`, `group`, `button`, `link`), por rótulo y por el texto del
 * propio dato. Nunca por clase ni por posición: el rediseño reordena secciones y cambia acordeones
 * por tarjetas, y eso no debe romper ninguna prueba de este archivo — solo debe romperlas que un
 * dato deje de estar.
 *
 * El contenido depende de la modalidad (el vendedor y su identidad solo existen en traspaso; la
 * placa preasignada solo en matrícula inicial), así que todo lo que dependa de eso se prueba
 * comparando las dos.
 */

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  getInstance: vi.fn(),
  getPrenda: vi.fn(),
  getActors: vi.fn(),
  getAttachments: vi.fn(),
  listBiometricExpediente: vi.fn(),
  getBiometricState: vi.fn(),
  getBiometricAuditByValidation: vi.fn(),
  downloadBiometricCertificado: vi.fn(),
  patchFieldValues: vi.fn(),
  generarFur: vi.fn(),
  generarImpronta: vi.fn(),
  generarConsolidado: vi.fn(),
  downloadAttachment: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  listTransitOffices: vi.fn(),
  runRnmc: vi.fn(),
  listMandateSigners: vi.fn(),
  setMandateSigner: vi.fn(),
  listFirmas: vi.fn(),
  listParticipantes: vi.fn(),
  completePlateFlow: vi.fn(),
  submitInstance: vi.fn(),
  iniciarBiometric: vi.fn(),
  simulateBiometric: vi.fn(),
  setPriority: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  getIdentitySendConflict: () => null,
}));

// Rango de placas del organismo (HU #10799/#10805/#10806): módulo aparte del cliente de trámites.
const plateMocks = vi.hoisted(() => ({
  getPlatePreassignStatus: vi.fn(),
  listAvailablePlatesForCompany: vi.fn(),
}));
vi.mock('@/lib/api/admin-plate-ranges', () => ({
  getPlatePreassignStatus: plateMocks.getPlatePreassignStatus,
  listAvailablePlatesForCompany: plateMocks.listAvailablePlatesForCompany,
}));

import { FirmaFurStep } from '@/components/operacion/FirmaFurStep';
import MatriculaResumen from '@/components/operacion/MatriculaResumen';

const INSTANCE = 'inst-1';

const VENDEDOR = {
  nombre: 'Ana Vendedora',
  documento: '111222',
  tipoDoc: 'CC',
  email: 'ana@example.com',
  telefono: '3001112233',
  direccion: 'Calle 1 # 2-3',
  ciudad: 'Medellín',
};

const COMPRADOR = {
  nombre: 'Beto Comprador',
  documento: '333444',
  tipoDoc: 'CC',
  email: 'beto@example.com',
  telefono: '3004445566',
  direccion: 'Carrera 10 # 20-30',
  ciudad: 'Bogotá',
};

const ESPECIFICACIONES = {
  clase: 'AUTOMOVIL',
  servicio: 'PARTICULAR',
  cilindraje: '1600',
  combustible: 'GASOLINA',
  carroceria: 'SEDAN',
  capacidad: '5',
  ejes: '2',
  estado: 'ACTIVO',
  motor: 'MOT-999',
  chasis: 'CHA-888',
  serie: 'SER-777',
};

const BIO_APROBADA: BiometricValidation = {
  id: 'val-1',
  partyRole: 'comprador',
  name: 'Beto Comprador',
  documentType: 'CC',
  documentNumber: '333444',
  email: 'beto@example.com',
  status: 'aprobado',
  intentos: 1,
  maxIntentos: 5,
  score: 95,
  expiresAt: '2026-09-20T00:00:00Z',
  validatedAt: '2026-08-12T00:00:00Z',
  expired: false,
  provider: 'mock',
  captureUrl: null,
};

const FUR_DOC: ProcedureAttachment = {
  id: 'att-fur',
  tipo: 'fur',
  filename: 'fur.pdf',
  mimetype: 'application/pdf',
  sizeBytes: 100,
  sha256: 'abc123',
  source: 'system',
  uploadedAt: '2026-08-12T00:00:00Z',
};

const IMPRONTA_DOC: ProcedureAttachment = {
  ...FUR_DOC,
  id: 'att-impronta',
  tipo: 'impronta',
  filename: 'impronta.pdf',
  sha256: 'def456',
};

/** Detalle de instancia con organismo ya resuelto (el modal de OT no se auto-abre). */
const DETALLE = {
  id: INSTANCE,
  referenceNumber: 'REF-1',
  status: 'borrador' as const,
  procedureTypeId: 'pt-1',
  tenantId: 't-1',
  createdAt: '2026-08-12T00:00:00Z',
  submittedAt: null,
  completedAt: null,
  statusHistory: [],
  fieldValues: [
    { formFieldId: null, fieldKey: 'transit_office_id', valueText: 'ot-1', valueJson: null, source: 'user' },
    { formFieldId: null, fieldKey: 'transit_office_code', valueText: '05001000', valueJson: null, source: 'user' },
    { formFieldId: null, fieldKey: 'transit_office_name', valueText: 'Secretaría de Movilidad de Medellín', valueJson: null, source: 'user' },
    { formFieldId: null, fieldKey: 'plate', valueText: 'PWL160', valueJson: null, source: 'user' },
    { formFieldId: null, fieldKey: 'vin', valueText: 'VIN-123456', valueJson: null, source: 'runt' },
    { formFieldId: null, fieldKey: 'vehicle_brand', valueText: 'RENAULT', valueJson: null, source: 'runt' },
    { formFieldId: null, fieldKey: 'vehicle_line', valueText: 'LOGAN', valueJson: null, source: 'runt' },
    { formFieldId: null, fieldKey: 'vehicle_year', valueText: '2020', valueJson: null, source: 'runt' },
    { formFieldId: null, fieldKey: 'vehicle_engine_number', valueText: 'MOT-999', valueJson: null, source: 'runt' },
    { formFieldId: null, fieldKey: 'vehicle_chassis', valueText: 'CHA-888', valueJson: null, source: 'runt' },
  ],
  actors: [
    { actorType: 'vendedor', documentType: 'CC', documentNumber: '111222', fullName: 'Ana Vendedora', email: 'ana@example.com' },
    { actorType: 'comprador', documentType: 'CC', documentNumber: '333444', fullName: 'Beto Comprador', email: 'beto@example.com' },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getInstance.mockResolvedValue(DETALLE);
  mocks.getPrenda.mockResolvedValue(null);
  mocks.getActors.mockResolvedValue([
    { rol: 'vendedor', tipoDocumento: 'CC', numeroDocumento: '111222', nombreCompleto: 'Ana Vendedora', email: 'ana@example.com', telefono: '3001112233', direccion: 'Calle 1 # 2-3', ciudad: 'Medellín' },
    { rol: 'comprador', tipoDocumento: 'CC', numeroDocumento: '333444', nombreCompleto: 'Beto Comprador', email: 'beto@example.com', telefono: '3004445566', direccion: 'Carrera 10 # 20-30', ciudad: 'Bogotá' },
  ]);
  mocks.getAttachments.mockResolvedValue([]);
  mocks.listBiometricExpediente.mockResolvedValue({ validations: [], provider: 'mock', firmaBaulPartes: [] });
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock', firmaBaulPartes: [] });
  mocks.getBiometricAuditByValidation.mockResolvedValue({ events: [], referencedFromOtherProcedure: false });
  mocks.downloadBiometricCertificado.mockResolvedValue({ blob: new Blob(['x']), filename: 'cert.pdf' });
  mocks.patchFieldValues.mockResolvedValue(DETALLE);
  mocks.generarFur.mockResolvedValue({ documents: [] });
  mocks.generarImpronta.mockResolvedValue({ attachmentId: 'imp', filename: 'i.pdf', sha256: 'x', radicado: 'R', hash: 'h' });
  mocks.generarConsolidado.mockResolvedValue({ regenerado: true, incompleto: false, documentosFaltantes: [] });
  mocks.downloadAttachment.mockResolvedValue({ blob: new Blob(['x']), filename: 'fur.pdf', mimetype: 'application/pdf' });
  mocks.fetchAttachmentPreviewUrl.mockResolvedValue({ url: 'https://storage/doc' });
  mocks.listTransitOffices.mockResolvedValue([]);
  mocks.runRnmc.mockResolvedValue([]);
  mocks.listMandateSigners.mockResolvedValue({ opciones: [], elegidoId: null, editable: true });
  mocks.setMandateSigner.mockResolvedValue(undefined);
  mocks.listFirmas.mockResolvedValue([]);
  mocks.listParticipantes.mockResolvedValue([]);
  mocks.completePlateFlow.mockResolvedValue({ warningMessage: null });
  mocks.submitInstance.mockResolvedValue({ id: INSTANCE, status: 'entregado' });
  mocks.setPriority.mockResolvedValue({ id: INSTANCE, prioritario: true });
  plateMocks.getPlatePreassignStatus.mockResolvedValue({ enabled: true });
  plateMocks.listAvailablePlatesForCompany.mockResolvedValue([]);
});

/** Monta el resumen aislado con datos completos; los overrides ajustan el escenario. */
function renderResumen(overrides: Partial<Parameters<typeof MatriculaResumen>[0]> = {}) {
  return render(
    <MatriculaResumen
      modalidad="traspaso"
      status="borrador"
      placa="PWL160"
      vehiculo="RENAULT LOGAN 2020"
      vin="VIN-123456"
      especificaciones={ESPECIFICACIONES}
      vendedor={VENDEDOR}
      comprador={COMPRADOR}
      archivosCount={0}
      identidadAprobada
      soat={{ estado: 'vigente', vencimiento: '2026-12-31' }}
      fechaTramite="2026-08-13"
      instanceId={INSTANCE}
      compradorBio={BIO_APROBADA}
      vendedorBio={{ ...BIO_APROBADA, id: 'val-2', partyRole: 'vendedor', name: 'Ana Vendedora', email: 'ana@example.com' }}
      {...overrides}
    />,
  );
}

describe('MatriculaResumen — inventario: cabecera y vehículo', () => {
  it('conserva el estado del trámite y la fecha con la que se radica', async () => {
    renderResumen();

    const resumen = screen.getByRole('region', { name: 'Resumen del trámite' });
    expect(within(resumen).getByText('Borrador')).toBeInTheDocument();

    const fecha = within(resumen).getByLabelText('Fecha del trámite') as HTMLInputElement;
    expect(fecha.value).toBe('2026-08-13');
    // Es un dato del trámite, no un campo: siempre el día de la radicación.
    expect(fecha).toBeDisabled();
    expect(fecha).toHaveAttribute('readonly');
  });

  it('la sección de vehículo conserva placa, SOAT, modelo, VIN y las once especificaciones', async () => {
    renderResumen();

    const vehiculo = screen.getByRole('region', { name: 'Vehículo' });
    expect(within(vehiculo).getByText('PWL160')).toBeInTheDocument();
    expect(within(vehiculo).getByText(/SOAT: Vigente/)).toBeInTheDocument();
    expect(within(vehiculo).getByText('Modelo / clase')).toBeInTheDocument();
    expect(within(vehiculo).getByText('RENAULT LOGAN 2020')).toBeInTheDocument();
    expect(within(vehiculo).getByText('VIN')).toBeInTheDocument();
    expect(within(vehiculo).getByText('VIN-123456')).toBeInTheDocument();

    expect(within(vehiculo).getByText('Especificaciones técnicas')).toBeInTheDocument();
    for (const etiqueta of [
      'Clase',
      'Servicio',
      'Cilindraje',
      'Combustible',
      'Carrocería',
      'Capacidad',
      'Ejes',
      'Estado',
      'N. Motor',
      'N. Chasis',
      'N. Serie',
    ]) {
      expect(within(vehiculo).getByText(etiqueta)).toBeInTheDocument();
    }
    // El cilindraje se muestra con su unidad aunque el RUNT lo mande pelado.
    expect(within(vehiculo).getByText('1600 cc')).toBeInTheDocument();
    expect(within(vehiculo).getByText('MOT-999')).toBeInTheDocument();
    expect(within(vehiculo).getByText('CHA-888')).toBeInTheDocument();
  });

  it('sin datos del vehículo lo dice en vez de dejar la sección muda', async () => {
    renderResumen({ placa: '', vehiculo: '', vin: '', especificaciones: {} });

    const vehiculo = screen.getByRole('region', { name: 'Vehículo' });
    expect(within(vehiculo).getByText('Sin datos de vehículo.')).toBeInTheDocument();
  });
});

describe('MatriculaResumen — inventario: actores del trámite', () => {
  // Traspaso tiene parte saliente y entrante; matrícula inicial solo entrante. Es la divergencia
  // legítima entre modalidades, y el rediseño no puede convertirla en «se me olvidó el vendedor».
  it('traspaso conserva vendedor y comprador; matrícula solo comprador', async () => {
    const { unmount } = renderResumen({ modalidad: 'traspaso' });
    expect(screen.getByRole('region', { name: 'Vendedor' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Comprador' })).toBeInTheDocument();
    unmount();

    renderResumen({ modalidad: 'matricula_inicial', vendedor: null, vendedorBio: null });
    expect(screen.queryByRole('region', { name: 'Vendedor' })).toBeNull();
    expect(screen.getByRole('region', { name: 'Comprador' })).toBeInTheDocument();
  });

  it.each([
    ['Vendedor', VENDEDOR],
    ['Comprador', COMPRADOR],
  ])('la sección de %s conserva sus seis datos de contacto', async (rol, actor) => {
    renderResumen();

    const seccion = screen.getByRole('region', { name: rol });
    for (const etiqueta of ['Nombre', 'Cédula', 'Email', 'Teléfono', 'Dirección', 'Ciudad']) {
      expect(within(seccion).getByText(etiqueta)).toBeInTheDocument();
    }
    // El nombre aparece dos veces a propósito (el dato y el sello de identidad verificada), así que
    // se cuenta en vez de exigir uno solo: lo que importa es que no desaparezca.
    expect(within(seccion).getAllByText(actor.nombre).length).toBeGreaterThan(0);
    expect(within(seccion).getByText(`CC ${actor.documento}`)).toBeInTheDocument();
    expect(within(seccion).getByText(actor.email)).toBeInTheDocument();
    expect(within(seccion).getByText(actor.telefono)).toBeInTheDocument();
    expect(within(seccion).getByText(actor.direccion)).toBeInTheDocument();
    expect(within(seccion).getByText(actor.ciudad)).toBeInTheDocument();
  });

  it('el actor jurídico rotula NIT y añade los datos de su representante legal', async () => {
    renderResumen({
      modalidad: 'matricula_inicial',
      vendedor: null,
      vendedorBio: null,
      comprador: { ...COMPRADOR, tipoDoc: 'NIT', documento: '900555666', nombre: 'Comercializadora SAS' },
      compradorBio: {
        ...BIO_APROBADA,
        name: 'Carlos Ramírez',
        documentType: 'CC',
        documentNumber: '79123456',
        email: 'carlos@valle.co',
      },
    });

    const comprador = screen.getByRole('region', { name: 'Comprador' });
    expect(within(comprador).getByText('NIT')).toBeInTheDocument();
    expect(within(comprador).getByText('NIT 900555666')).toBeInTheDocument();

    expect(within(comprador).getByText('Representante legal')).toBeInTheDocument();
    // También sale en el sello de identidad: es él quien valida por la persona jurídica.
    expect(within(comprador).getAllByText('Carlos Ramírez').length).toBeGreaterThan(0);
    expect(within(comprador).getByText('Tipo doc')).toBeInTheDocument();
    expect(within(comprador).getByText('Número')).toBeInTheDocument();
    expect(within(comprador).getByText('79123456')).toBeInTheDocument();
  });

  it('con la identidad aprobada muestra el sello y el certificado descargable de cada parte', async () => {
    renderResumen();

    const vendedor = screen.getByRole('region', { name: 'Vendedor' });
    const comprador = screen.getByRole('region', { name: 'Comprador' });

    for (const seccion of [vendedor, comprador]) {
      expect(within(seccion).getByText('Validación de identidad')).toBeInTheDocument();
      expect(within(seccion).getByText(/Identidad verificada — 95\/100/)).toBeInTheDocument();
    }
    // Un certificado por parte, rotulado con la parte a la que pertenece.
    expect(
      within(vendedor).getByRole('button', { name: 'Certificado ID · Vendedor' }),
    ).toBeEnabled();
    expect(
      within(comprador).getByRole('button', { name: 'Certificado ID · Comprador' }),
    ).toBeEnabled();
  });

  it('sin identidad aprobada el certificado sigue anunciado, pero no descargable', async () => {
    renderResumen({
      modalidad: 'matricula_inicial',
      vendedor: null,
      vendedorBio: null,
      compradorBio: null,
      instanceId: null,
    });

    const comprador = screen.getByRole('region', { name: 'Comprador' });
    const boton = within(comprador).getByRole('button', { name: 'Certificado ID · Comprador' });
    expect(boton).toBeDisabled();
    expect(
      within(comprador).getByText('El certificado estará disponible cuando la validación sea aprobada.'),
    ).toBeInTheDocument();
  });

  it('con la identidad pendiente la captura se embebe dentro de la parte', async () => {
    renderResumen({ compradorBio: null, vendedorBio: null });

    // La tarjeta de la parte pasa a hospedar la biométrica en vez de repetir el campo Validación.
    const comprador = screen.getByRole('region', { name: 'Comprador' });
    expect(
      await within(comprador).findByRole('group', { name: 'Biométrica Comprador' }),
    ).toBeInTheDocument();
    expect(within(comprador).queryByText('Validación de identidad')).toBeNull();
  });

  it('la parte cubierta por la firma del baúl se anuncia como tal y sin certificado', async () => {
    renderResumen({ firmaBaulPartes: ['vendedor'] });

    const vendedor = screen.getByRole('region', { name: 'Vendedor' });
    expect(within(vendedor).getByText('Firma electrónica (baúl)')).toBeInTheDocument();
    expect(within(vendedor).queryByRole('button', { name: /Certificado ID/ })).toBeNull();
  });
});

describe('MatriculaResumen — inventario: mandatario, transformaciones y prenda', () => {
  it('el mandatario que firma conserva sus candidatos y el estado de su firma', async () => {
    mocks.listMandateSigners.mockResolvedValue({
      opciones: [
        { id: 'm-1', nombre: 'Juan Mandatario', tipoDocumento: 'CC', documento: '555', firmaBaulVigente: true, identidadVigente: false, firmaFisica: false, identidadHasta: null },
        { id: 'm-2', nombre: 'Luisa Apoderada', tipoDocumento: 'CC', documento: '666', firmaBaulVigente: false, identidadVigente: false, firmaFisica: false, identidadHasta: null },
      ],
      elegidoId: 'm-1',
      editable: true,
    });
    const user = userEvent.setup();
    renderResumen();

    // Plegado por defecto: el sistema lo resuelve solo, así que no reclama atención.
    await user.click(await screen.findByRole('button', { name: 'Mandatario' }));

    const mandatario = screen.getByRole('region', { name: 'Mandatario que firma' });
    expect(within(mandatario).getByRole('radio', { name: /Juan Mandatario/ })).toBeChecked();
    expect(within(mandatario).getByRole('radio', { name: /Luisa Apoderada/ })).toBeInTheDocument();
    expect(within(mandatario).getByText('Firma del baúl vigente')).toBeInTheDocument();
    expect(within(mandatario).getByText(/Sin firma del baúl ni identidad vigentes/)).toBeInTheDocument();
  });

  it('sin candidatos de mandato no se pinta una sección vacía', async () => {
    renderResumen();

    await waitFor(() => expect(mocks.listMandateSigners).toHaveBeenCalled());
    expect(screen.queryByRole('button', { name: 'Mandatario' })).toBeNull();
  });

  it('las transformaciones declaradas conservan qué cambió y a qué valor', async () => {
    renderResumen({ transformaciones: ['Color: NEGRO', 'Combustible: DIESEL'] });

    const transformaciones = screen.getByLabelText('Transformaciones declaradas');
    expect(within(transformaciones).getByText('Color')).toBeInTheDocument();
    expect(within(transformaciones).getByText('NEGRO')).toBeInTheDocument();
    expect(within(transformaciones).getByText('Combustible')).toBeInTheDocument();
    expect(within(transformaciones).getByText('DIESEL')).toBeInTheDocument();
  });

  it('la prenda conserva decisión, acreedor y su documento de soporte', async () => {
    renderResumen({
      prenda: {
        decisionLabel: 'Registrar prenda',
        acreedorNombre: 'Banco Demo',
        acreedorDocumento: '900111222',
        documentoLabel: 'Certificado / registro de prenda',
        documento: { id: 'att-prenda', tipo: 'prenda', filename: 'prenda.pdf', mimetype: 'application/pdf' },
      },
    });

    const prenda = screen.getByLabelText('Prenda o gravamen');
    expect(within(prenda).getByText('Decisión')).toBeInTheDocument();
    expect(within(prenda).getByText('Registrar prenda')).toBeInTheDocument();
    expect(within(prenda).getByText('Acreedor (beneficiario)')).toBeInTheDocument();
    expect(within(prenda).getByText('Banco Demo')).toBeInTheDocument();
    expect(within(prenda).getByText('NIT / documento del acreedor')).toBeInTheDocument();
    expect(within(prenda).getByText('900111222')).toBeInTheDocument();
    expect(within(prenda).getByText('Certificado / registro de prenda')).toBeInTheDocument();
    expect(within(prenda).getByRole('button', { name: 'Ver prenda.pdf' })).toBeInTheDocument();
  });

  it('la prenda sin soporte cargado lo dice en vez de callarlo', async () => {
    renderResumen({
      prenda: {
        decisionLabel: 'Registrar prenda',
        acreedorNombre: null,
        acreedorDocumento: null,
        documentoLabel: 'Certificado / registro de prenda',
        documento: null,
      },
    });

    const prenda = screen.getByLabelText('Prenda o gravamen');
    expect(within(prenda).getByText('Sin documento cargado')).toBeInTheDocument();
  });

  it('sin transformaciones ni prenda esas secciones no existen', async () => {
    renderResumen();

    expect(screen.queryByLabelText('Transformaciones declaradas')).toBeNull();
    expect(screen.queryByLabelText('Prenda o gravamen')).toBeNull();
  });
});

describe('FirmaFurStep — inventario: expediente y documentos generados', () => {
  it('el paso monta el resumen y el expediente digital', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    expect(await screen.findByRole('region', { name: 'Resumen del trámite' })).toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'Expediente digital' })).toBeInTheDocument();
  });

  it('lista los documentos generados con su nombre y su huella', async () => {
    mocks.getAttachments.mockResolvedValue([FUR_DOC, IMPRONTA_DOC]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    const documentos = await screen.findByRole('list', { name: 'Documentos del expediente (visor)' });
    expect(within(documentos).getByText(/fur\.pdf/)).toBeInTheDocument();
    expect(within(documentos).getByText(/impronta\.pdf/)).toBeInTheDocument();
    // La huella del documento es lo que permite demostrar que no se alteró.
    expect(within(documentos).getByText(/SHA-256 abc123/)).toBeInTheDocument();
    // Cada documento se puede abrir.
    expect(within(documentos).getByRole('button', { name: 'Ver fur.pdf' })).toBeInTheDocument();
    expect(within(documentos).getByRole('button', { name: 'Ver impronta.pdf' })).toBeInTheDocument();
  });

  it('sin documentos todavía lo dice, y conserva el consolidado como acción', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    const expediente = await screen.findByRole('region', { name: 'Expediente digital' });
    expect(within(expediente).getByText('No se han cargado documentos.')).toBeInTheDocument();
    expect(within(expediente).getByText('Expediente consolidado')).toBeInTheDocument();
    expect(
      await within(expediente).findByRole('button', { name: 'Descargar todo · Expediente consolidado (PDF)' }),
    ).toBeInTheDocument();
  });

  it('con consolidado ya generado ofrece además re-generarlo', async () => {
    mocks.getAttachments.mockResolvedValue([
      { ...FUR_DOC, id: 'att-cons', tipo: 'consolidado', filename: 'consolidado.pdf' },
    ]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    expect(
      await screen.findByRole('button', { name: 'Re-generar expediente consolidado' }),
    ).toBeInTheDocument();
  });

  // El texto del consolidado enumera lo que lleva dentro, y en traspaso lleva un documento más.
  it('el consolidado anuncia el contrato de compraventa solo en traspaso', async () => {
    const { unmount } = render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);
    expect(
      await screen.findByText(/incluye el contrato de compraventa/),
    ).toBeInTheDocument();
    unmount();

    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByRole('region', { name: 'Expediente digital' });
    expect(screen.queryByText(/incluye el contrato de compraventa/)).toBeNull();
  });
});

describe('FirmaFurStep — inventario: placa preasignada (solo matrícula inicial)', () => {
  it('matrícula ofrece el rango del organismo, su buscador y el dígito de preferencia', async () => {
    plateMocks.listAvailablePlatesForCompany.mockResolvedValue([
      { id: 'p-1', plate: 'ABC123' },
      { id: 'p-2', plate: 'ABC124' },
    ]);
    mocks.getInstance.mockResolvedValue({
      ...DETALLE,
      // Sin placa elegida todavía: es cuando aplica el selector.
      fieldValues: DETALLE.fieldValues.filter((f) => f.fieldKey !== 'plate'),
    });
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    const placa = await screen.findByRole('region', { name: 'Placa preasignada' });
    expect(within(placa).getByLabelText('Buscar placa disponible')).toBeInTheDocument();
    expect(await within(placa).findByRole('button', { name: 'ABC123' })).toBeInTheDocument();
    expect(within(placa).getByRole('button', { name: 'ABC124' })).toBeInTheDocument();

    // HU #10805 — si se radica sin placa, el dígito de preferencia es la guía para el OT.
    const digito = within(placa).getByLabelText('Dígito de preferencia de placa');
    expect(within(digito).getAllByRole('option')).toHaveLength(11);
    expect(within(digito).getByRole('option', { name: 'Sin preferencia' })).toBeInTheDocument();
    expect(within(digito).getByRole('option', { name: 'Termina en 7' })).toBeInTheDocument();
  });

  it('con una placa ya elegida la muestra y deja cambiarla o quitarla', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    const placa = await screen.findByRole('region', { name: 'Placa preasignada' });
    expect(within(placa).getByText(/Placa seleccionada:/)).toBeInTheDocument();
    expect(within(placa).getByText('PWL160')).toBeInTheDocument();
    expect(within(placa).getByRole('button', { name: 'Cambiar' })).toBeInTheDocument();
    expect(within(placa).getByRole('button', { name: 'Quitar placa' })).toBeInTheDocument();
  });

  it('si el vehículo ya trae placa del RUNT explica que no aplica la preasignación', async () => {
    mocks.getInstance.mockResolvedValue({
      ...DETALLE,
      fieldValues: DETALLE.fieldValues.map((f) =>
        f.fieldKey === 'plate' ? { ...f, source: 'consultation' } : f,
      ),
    });
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    const placa = await screen.findByRole('region', { name: 'Placa preasignada' });
    expect(within(placa).getByText(/No aplica la preasignación de placa\./)).toBeInTheDocument();
  });

  it('traspaso no tiene placa preasignada: el vehículo ya la tiene', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    await screen.findByRole('region', { name: 'Resumen del trámite' });
    expect(screen.queryByRole('region', { name: 'Placa preasignada' })).toBeNull();
  });
});

describe('FirmaFurStep — inventario: consulta RNMC de medidas correctivas', () => {
  const check = (key: string, status: 'ok' | 'warn', message: string) => ({
    key,
    label: 'Medidas correctivas (Policía)',
    status,
    source: 'verifik_rnmc',
    message,
    action: null,
  });

  it('traspaso consulta a las dos partes; matrícula solo al comprador', async () => {
    mocks.runRnmc.mockResolvedValue([
      check('rnmc_comprador_medidas_correctivas', 'ok', 'Sin medidas correctivas registradas en el RNMC'),
      check('rnmc_vendedor_medidas_correctivas', 'warn', '1 medida correctiva pendiente'),
    ]);
    const { unmount } = render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" rnmcEnabled />);

    expect(await screen.findByText('Consulta RNMC — Medidas correctivas')).toBeInTheDocument();
    const resultados = await screen.findByRole('list', { name: 'Resultados RNMC por actor' });
    expect(within(resultados).getByText(/\(comprador\)/)).toBeInTheDocument();
    expect(within(resultados).getByText(/\(vendedor\)/)).toBeInTheDocument();
    expect(within(resultados).getByText('1 medida correctiva pendiente')).toBeInTheDocument();
    unmount();

    mocks.runRnmc.mockResolvedValue([
      check('rnmc_comprador_medidas_correctivas', 'ok', 'Sin medidas correctivas registradas en el RNMC'),
    ]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" rnmcEnabled />);
    const soloComprador = await screen.findByRole('list', { name: 'Resultados RNMC por actor' });
    expect(within(soloComprador).getByText(/\(comprador\)/)).toBeInTheDocument();
    expect(within(soloComprador).queryByText(/\(vendedor\)/)).toBeNull();
  });

  it('sin RNMC aplicable no se consulta ni se anuncia la sección', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    await screen.findByRole('region', { name: 'Expediente digital' });
    expect(mocks.runRnmc).not.toHaveBeenCalled();
    expect(screen.queryByText('Consulta RNMC — Medidas correctivas')).toBeNull();
  });
});

/**
 * DÓNDE VIVEN HOY LAS ACCIONES DE RADICACIÓN.
 *
 * Dos piezas que se buscan en este paso por costumbre y que ya NO están aquí:
 *
 *  · Las observaciones del FUR (`fur_observations`) se capturan en el paso de Requisitos, al final,
 *    cuando el gestor ya sabe qué documentos quedaron adjuntos.
 *  · El envío a tránsito lo dispara el pie del asistente (Preparar / Radicar), no el paso: tenerlo
 *    en los dos sitios significaba dos botones capaces de radicar el mismo trámite.
 *
 * Se fija por escrito para que el rediseño no las «recupere» y acabemos con el dato duplicado en
 * dos pantallas o con dos caminos distintos para radicar.
 */
describe('FirmaFurStep — inventario: acciones del cierre del trámite', () => {
  it('no duplica ni las observaciones del FUR ni el envío a tránsito', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    await screen.findByRole('region', { name: 'Resumen del trámite' });
    expect(screen.queryByLabelText(/Observaciones del trámite/)).toBeNull();
    expect(screen.queryByRole('region', { name: 'Envío a tránsito' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Enviar a tránsito' })).toBeNull();
    expect(mocks.submitInstance).not.toHaveBeenCalled();
  });

  it('con la placa ya asignada por el OT conserva el cierre del gestor', async () => {
    mocks.getInstance.mockResolvedValue({ ...DETALLE, plateFlowStatus: 'asignado' });
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    expect(
      await screen.findByText('Procesar trámite (Asignado → Terminado)'),
    ).toBeInTheDocument();
    // Los dos checks opcionales del gestor y el avance que desbloquea al OT.
    expect(screen.getByLabelText('SOAT pagado')).toBeInTheDocument();
    expect(screen.getByLabelText('Impuesto departamental pagado')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Marcar como Terminado' })).toBeInTheDocument();
  });

  it('sin ese sub-estado no ofrece el cierre del gestor', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await screen.findByRole('region', { name: 'Resumen del trámite' });
    expect(screen.queryByRole('button', { name: 'Marcar como Terminado' })).toBeNull();
  });

  it('en trámite aprobado la documentación es definitiva y no se regenera', async () => {
    mocks.getInstance.mockResolvedValue({ ...DETALLE, status: 'aprobado' });
    mocks.getAttachments.mockResolvedValue([FUR_DOC]);
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    expect(
      await screen.findByText(/su documentación es definitiva\. Puedes consultarla y descargarla\./),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Re-generar expediente consolidado' })).toBeNull();
    // Pero los documentos siguen listados y descargables.
    const documentos = screen.getByRole('list', { name: 'Documentos del expediente (visor)' });
    expect(within(documentos).getByRole('button', { name: 'Ver fur.pdf' })).toBeInTheDocument();
  });
});

// Auditoría del líder de diseño (No aprobado) — la cuarta parte del contenido que faltaba.
describe('MatriculaResumen — inventario: tracking de validación de identidad por actor', () => {
  it('traspaso monta una tabla de tracking por actor, con su nombre y puntaje', async () => {
    renderResumen();

    const tracking = await screen.findByRole('region', {
      name: 'Tracking de validación de identidad',
    });
    expect(within(tracking).getByText('Ana Vendedora')).toBeInTheDocument();
    expect(within(tracking).getByText('Beto Comprador')).toBeInTheDocument();
    // Puntaje tintado (StatusBadge tone="success"), nunca la píldora sólida de la propuesta.
    expect(within(tracking).getAllByText('Puntaje 95/100')).toHaveLength(2);
    await waitFor(() => expect(mocks.getBiometricAuditByValidation).toHaveBeenCalled());
  });

  it('matrícula inicial monta solo la tabla del comprador', async () => {
    renderResumen({ modalidad: 'matricula_inicial', vendedor: null, vendedorBio: null });

    const tracking = await screen.findByRole('region', {
      name: 'Tracking de validación de identidad',
    });
    expect(within(tracking).getByText('Beto Comprador')).toBeInTheDocument();
    expect(within(tracking).queryByText('Ana Vendedora')).toBeNull();
    expect(within(tracking).getAllByText('Puntaje 95/100')).toHaveLength(1);
  });

  it('sin ninguna validación biométrica no se monta la sección', async () => {
    renderResumen({ compradorBio: null, vendedorBio: null });
    await screen.findByRole('region', { name: 'Resumen del trámite' });
    expect(
      screen.queryByRole('region', { name: 'Tracking de validación de identidad' }),
    ).toBeNull();
  });

  it('sin puntaje todavía (validación pendiente) no inventa un número', async () => {
    renderResumen({
      compradorBio: { ...BIO_APROBADA, status: 'en_proceso', score: null, captureUrl: null },
      vendedorBio: null,
      modalidad: 'matricula_inicial',
      vendedor: null,
    });

    const tracking = await screen.findByRole('region', {
      name: 'Tracking de validación de identidad',
    });
    expect(within(tracking).queryByText(/Puntaje/)).toBeNull();
  });

  it('con el enlace de captura pendiente lo muestra en monoespaciada, con botón de copiar', async () => {
    renderResumen({
      compradorBio: {
        ...BIO_APROBADA,
        status: 'en_proceso',
        score: null,
        captureUrl: 'https://kyverum.flit.io/captura/TRM-2026-000095',
      },
      vendedorBio: null,
      modalidad: 'matricula_inicial',
      vendedor: null,
    });

    const tracking = await screen.findByRole('region', {
      name: 'Tracking de validación de identidad',
    });
    const enlace = within(tracking).getByLabelText('Enlace de captura de Beto Comprador');
    expect(enlace).toHaveValue('https://kyverum.flit.io/captura/TRM-2026-000095');
    expect(within(tracking).getByRole('button', { name: 'Copiar enlace' })).toBeInTheDocument();
  });
});

describe('MatriculaResumen — inventario: trámites solicitados (espejo del FUR)', () => {
  it('abre el consolidado con el texto final que se estampará en el FUR', async () => {
    renderResumen({ observacionesFur: 'Traspaso Estándar. Radicación electrónica.' });

    expect(screen.getByText('Trámites solicitados')).toBeInTheDocument();
    expect(
      screen.getByText('Traspaso Estándar. Radicación electrónica.'),
    ).toBeInTheDocument();
  });

  it('sin observaciones del FUR todavía no se pinta el bloque', async () => {
    renderResumen({ observacionesFur: null });
    expect(screen.queryByText('Trámites solicitados')).toBeNull();
  });

  it('es de solo lectura: no monta un segundo campo editable de observaciones', async () => {
    renderResumen({ observacionesFur: 'Matrícula Inicial.' });
    // El contrato del paso: la captura de observaciones vive en Requisitos, no aquí.
    expect(screen.queryByLabelText(/Observaciones del trámite/)).toBeNull();
    expect(screen.queryByRole('textbox', { name: /Observaciones/ })).toBeNull();
  });
});

describe('MatriculaResumen — inventario: trámite prioritario en la cabecera', () => {
  it('conserva el interruptor y avisa al cambiarlo', async () => {
    const onPrioritarioChange = vi.fn();
    renderResumen({ prioritario: false, onPrioritarioChange });

    const interruptor = screen.getByRole('button', { name: 'Trámite Prioritario' });
    expect(interruptor).toHaveAttribute('aria-pressed', 'false');

    await userEvent.setup().click(interruptor);
    expect(onPrioritarioChange).toHaveBeenCalledWith(true);
  });

  it('refleja el estado activado', async () => {
    renderResumen({ prioritario: true, onPrioritarioChange: vi.fn() });
    expect(
      screen.getByRole('button', { name: 'Trámite Prioritario' }),
    ).toHaveAttribute('aria-pressed', 'true');
  });

  it('sin handler (trámite aún sin crear) no se ofrece el interruptor', async () => {
    renderResumen();
    expect(screen.queryByRole('button', { name: 'Trámite Prioritario' })).toBeNull();
  });
});

describe('FirmaFurStep — inventario: organismo de tránsito también en traspaso', () => {
  it('traspaso con OT resuelto por el RUNT también lo muestra (antes solo matrícula)', async () => {
    mocks.getInstance.mockResolvedValue({
      ...DETALLE,
      fieldValues: [
        { formFieldId: null, fieldKey: 'transit_office_id', valueText: 'ot-9', valueJson: null, source: 'consultation' },
        { formFieldId: null, fieldKey: 'transit_office_code', valueText: '11001', valueJson: null, source: 'consultation' },
        { formFieldId: null, fieldKey: 'transit_office_name', valueText: 'Secretaría Distrital de Movilidad de Bogotá', valueJson: null, source: 'consultation' },
      ],
    });
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    await screen.findByRole('region', { name: 'Resumen del trámite' });
    expect(screen.getByText('Organismo de tránsito')).toBeInTheDocument();
    expect(
      screen.getByText('Secretaría Distrital de Movilidad de Bogotá'),
    ).toBeInTheDocument();
    // El dígito de preferencia de placa sigue exclusivo de matrícula: en traspaso el vehículo ya
    // tiene placa.
    expect(screen.queryByRole('region', { name: 'Placa preasignada' })).toBeNull();
  });

  it('matrícula sigue mostrando organismo + preasignación de placa emparejados', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await screen.findByRole('region', { name: 'Resumen del trámite' });
    expect(screen.getByText('Organismo de tránsito')).toBeInTheDocument();
    expect(
      await screen.findByRole('region', { name: 'Placa preasignada' }),
    ).toBeInTheDocument();
  });
});

describe('FirmaFurStep — inventario: trazabilidad cronológica del trámite', () => {
  it('monta la línea de tiempo con el estado, su origen y el motivo, en una sola frase', async () => {
    mocks.getInstance.mockResolvedValue({
      ...DETALLE,
      statusHistory: [
        { fromStatus: null, toStatus: 'borrador', changedAt: '2026-08-01T08:00:00Z', reason: null },
        {
          fromStatus: 'entregado',
          toStatus: 'rechazado',
          changedAt: '2026-08-05T10:00:00Z',
          reason: 'Cargar documentos',
        },
      ],
    });
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="traspaso" />);

    const timeline = await screen.findByRole('region', {
      name: 'Línea de tiempo del expediente',
    });
    expect(within(timeline).getByText('Borrador')).toBeInTheDocument();
    expect(
      within(timeline).getByText('Rechazado desde Entregado (Cargar documentos)'),
    ).toBeInTheDocument();
  });

  it('sin historial todavía lo dice en vez de dejar la sección muda', async () => {
    render(<FirmaFurStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    const timeline = await screen.findByRole('region', {
      name: 'Línea de tiempo del expediente',
    });
    expect(within(timeline).getByText('Sin eventos registrados todavía.')).toBeInTheDocument();
  });
});
