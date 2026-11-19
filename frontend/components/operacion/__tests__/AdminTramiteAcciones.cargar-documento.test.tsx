// Bug #14604 — «Actualizar documento» en el menú del admin: visible solo con el permiso, habilitado en
// cualquier estado, observación obligatoria, envío multipart y errores del backend mapeados.
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ToastProvider } from '@/components/admin/Toast';
import { useAdminTramiteAcciones } from '../AdminTramiteAcciones';
import { mensajeErrorCargaDocumento, validarArchivoCarga } from '../AdminCargarDocumentoModal';
import { TramitesApiError } from '@/lib/api/tramites-client';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

const permissionsState = { permissions: [] as string[], isSuperAdmin: false };
vi.mock('@/hooks/usePermissions', () => ({ usePermissions: () => permissionsState }));

const listarTipos = vi.fn();
const getAttachments = vi.fn();
const cargarDocumento = vi.fn();
vi.mock('@/lib/api/tramites-client', async () => {
  const actual = await vi.importActual<typeof import('@/lib/api/tramites-client')>('@/lib/api/tramites-client');
  return {
    ...actual,
    tramitesClient: {
      ...actual.tramitesClient,
      adminListarTiposDocumento: (...a: unknown[]) => listarTipos(...a),
      getAttachments: (...a: unknown[]) => getAttachments(...a),
      adminCargarDocumento: (...a: unknown[]) => cargarDocumento(...a),
    },
  };
});

function Harness({ item, onChanged }: { item: InstanceSummary; onChanged: () => void }) {
  const { items, modals } = useAdminTramiteAcciones({ item, isAdmin: false, onChanged });
  return (
    <div>
      {items.map((i) => (
        <button key={i.key} type="button" disabled={i.disabled} onClick={i.onSelect}>
          {i.label}
        </button>
      ))}
      {modals}
    </div>
  );
}

const base = {
  id: 'inst-1',
  referenceNumber: '1',
  tenantId: 't-1',
  estado: 'entregado',
} as unknown as InstanceSummary;

const TIPOS = [
  { tipo: 'soat', nombre: 'SOAT' },
  { tipo: 'factura', nombre: 'Factura' },
];

function pdf(name = 'a.pdf', type = 'application/pdf', size = 10) {
  const f = new File(['x'], name, { type });
  Object.defineProperty(f, 'size', { value: size });
  return f;
}

async function abrir(item = base, onChanged = vi.fn()) {
  render(
    <ToastProvider>
      <Harness item={item} onChanged={onChanged} />
    </ToastProvider>,
  );
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Actualizar documento' }));
  return { user, onChanged };
}

const botonEnviar = () => screen.getAllByRole('button', { name: 'Actualizar documento' }).at(-1)!;

describe('Actualizar documento (acciones del admin)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    permissionsState.permissions = ['AdminTramiteCargarDocumento'];
    permissionsState.isSuperAdmin = false;
    listarTipos.mockResolvedValue(TIPOS);
    getAttachments.mockResolvedValue([]);
  });

  it('no aparece sin el permiso', () => {
    permissionsState.permissions = [];
    render(
      <ToastProvider>
        <Harness item={base} onChanged={vi.fn()} />
      </ToastProvider>,
    );
    expect(screen.queryByRole('button', { name: 'Actualizar documento' })).not.toBeInTheDocument();
  });

  it.each(['entregado', 'aprobado', 'anulado', 'revocado', 'borrador'])(
    'está habilitada con el estado %s',
    (estado) => {
      render(
        <ToastProvider>
          <Harness item={{ ...base, estado } as InstanceSummary} onChanged={vi.fn()} />
        </ToastProvider>,
      );
      expect(screen.getByRole('button', { name: 'Actualizar documento' })).toBeEnabled();
    },
  );

  it('el botón principal queda deshabilitado mientras la observación esté vacía', async () => {
    const { user } = await abrir();
    await screen.findByLabelText('Tipo de documento');
    await user.selectOptions(screen.getByLabelText('Tipo de documento'), 'soat');
    await user.upload(screen.getByTestId('admin-cargar-doc-archivo'), pdf());
    expect(botonEnviar()).toBeDisabled();

    await user.type(screen.getByLabelText('Observación'), '   ');
    expect(botonEnviar()).toBeDisabled();
    await user.type(screen.getByLabelText('Observación'), 'Corrección');
    expect(botonEnviar()).toBeEnabled();
  });

  it('envía tipo, archivo y observación, avisa y refresca', async () => {
    cargarDocumento.mockResolvedValue({ attachmentId: 'a', tipo: 'soat', filename: 'a.pdf', previousAttachmentIds: [] });
    const { user, onChanged } = await abrir();
    await screen.findByLabelText('Tipo de documento');
    await user.selectOptions(screen.getByLabelText('Tipo de documento'), 'soat');
    await user.upload(screen.getByTestId('admin-cargar-doc-archivo'), pdf());
    await user.type(screen.getByLabelText('Observación'), '  Soporte faltante  ');
    await user.click(botonEnviar());

    await waitFor(() => expect(cargarDocumento).toHaveBeenCalledTimes(1));
    const [id, input] = cargarDocumento.mock.calls[0]!;
    expect(id).toBe('inst-1');
    expect(input.tipo).toBe('soat');
    expect(input.observacion).toBe('Soporte faltante');
    expect(input.file.name).toBe('a.pdf');
    expect(await screen.findByText('Documento actualizado.')).toBeInTheDocument();
    expect(onChanged).toHaveBeenCalled();
  });

  it('muestra el aviso de versión anterior solo si el tipo tiene documento vigente', async () => {
    getAttachments.mockResolvedValue([
      { id: '1', tipo: 'SOAT', filename: 'v.pdf', uploadedAt: '2026-01-01T00:00:00Z' },
      { id: '2', tipo: 'factura', filename: 'f.pdf', uploadedAt: '2026-01-01T00:00:00Z', isHistorico: true },
    ]);
    const { user } = await abrir();
    await screen.findByLabelText('Tipo de documento');
    await user.selectOptions(screen.getByLabelText('Tipo de documento'), 'factura');
    expect(screen.queryByText(/quedará como versión anterior/)).not.toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Tipo de documento'), 'soat');
    expect(screen.getByText('El documento actual quedará como versión anterior.')).toBeInTheDocument();
  });

  it('un error del backend se muestra en el modal y no refresca', async () => {
    cargarDocumento.mockRejectedValue(new TramitesApiError(422, 'detalle', { title: 'tipo_no_habilitado' }));
    const { user, onChanged } = await abrir();
    await screen.findByLabelText('Tipo de documento');
    await user.selectOptions(screen.getByLabelText('Tipo de documento'), 'soat');
    await user.upload(screen.getByTestId('admin-cargar-doc-archivo'), pdf());
    await user.type(screen.getByLabelText('Observación'), 'x');
    await user.click(botonEnviar());

    expect(
      (await screen.findAllByText('Ese tipo de documento no está habilitado para este trámite.')).length,
    ).toBeGreaterThan(0);
    expect(onChanged).not.toHaveBeenCalled();
  });

  it('estado vacío cuando no hay tipos', async () => {
    listarTipos.mockResolvedValue([]);
    await abrir();
    expect(await screen.findByText('No hay tipos de documento habilitados para este trámite.')).toBeInTheDocument();
  });

  it('estado de error al cargar los tipos, con reintento', async () => {
    listarTipos.mockRejectedValueOnce(new Error('Falla de red'));
    const { user } = await abrir();
    expect(await screen.findByText('Falla de red')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /reintentar/i }));
    expect(await screen.findByLabelText('Tipo de documento')).toBeInTheDocument();
  });
});

describe('validación de archivo y mensajes', () => {
  it('rechaza formato no permitido, vacío y mayor a 20 MB', () => {
    expect(validarArchivoCarga(pdf('a.exe', 'application/x-msdownload'))).toMatch(/Formato no permitido/);
    expect(validarArchivoCarga(pdf('a.pdf', 'application/pdf', 21 * 1024 * 1024))).toMatch(/20 MB/);
    expect(validarArchivoCarga(pdf('a.pdf', 'application/pdf', 0))).toMatch(/vacío/);
    expect(validarArchivoCarga(pdf('a.png', 'image/png'))).toBeNull();
  });

  it('mapea los códigos del backend y cae al detail', () => {
    expect(mensajeErrorCargaDocumento(new TramitesApiError(400, 'd', { title: 'file_too_large' }))).toMatch(/20 MB/);
    expect(mensajeErrorCargaDocumento(new TramitesApiError(400, 'd', { title: 'observacion_requerida' }))).toMatch(
      /observación/,
    );
    expect(mensajeErrorCargaDocumento(new TramitesApiError(409, 'Trámite bloqueado por FLITO', null))).toBe(
      'Trámite bloqueado por FLITO',
    );
    expect(mensajeErrorCargaDocumento('x')).toMatch(/No se pudo cargar/);
  });
});
