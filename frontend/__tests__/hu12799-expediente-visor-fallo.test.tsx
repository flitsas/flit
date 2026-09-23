/**
 * HU #12799 (Épica #12760) — el gestor ve un aviso cuando falla la regeneración del consolidado
 * (ExpedienteVisor). Contrato #12798: el POST responde con éxito, trae el PDF ANTERIOR,
 * `regenerado: false` y `avisosCascada: ["consolidado: <causa>"]`.
 *
 * Uso de ejemplo:
 *   <ExpedienteVisor instanceId="inst-1" attachments={[consolidado]}
 *     consolidadoWizard={{ estado: 'desactualizado', generadoEn: '…', … }} />
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type {
  ConsolidadoVigencia,
  GenerarConsolidadoResult,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  generarConsolidado: vi.fn(),
  entregarConsolidado: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  openLoadingDocumentTab: vi.fn(() => ({ closed: false, close: vi.fn() }) as unknown as Window),
  openObjectUrlInWindow: vi.fn(),
  showDocumentTabError: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    generarConsolidado: mocks.generarConsolidado,
    entregarConsolidado: mocks.entregarConsolidado,
    fetchAttachmentPreviewUrl: mocks.fetchAttachmentPreviewUrl,
    downloadAttachment: mocks.downloadAttachment,
  },
}));

vi.mock('@/lib/documents/open-document-tab', () => ({
  openLoadingDocumentTab: mocks.openLoadingDocumentTab,
  openObjectUrlInWindow: mocks.openObjectUrlInWindow,
  showDocumentTabError: mocks.showDocumentTabError,
}));

import ExpedienteVisor from '@/components/operacion/ExpedienteVisor';

const INSTANCE = 'inst-12799';
// 15:05 UTC = 10:05 Bogotá. Datos ficticios.
const CONSERVADO = '2026-09-20T15:05:00Z';
const CONSOLIDADO: ProcedureAttachment = {
  id: 'att-anterior',
  tipo: 'consolidado',
  filename: 'consolidado.pdf',
  mimetype: 'application/pdf',
} as ProcedureAttachment;

function vigencia(overrides: Partial<ConsolidadoVigencia> = {}): ConsolidadoVigencia {
  return {
    estado: 'vigente',
    generadoEn: CONSERVADO,
    origen: 'system',
    definitivo: false,
    modo: null,
    ...overrides,
  };
}

function respuesta(overrides: Partial<GenerarConsolidadoResult> = {}): GenerarConsolidadoResult {
  return {
    document: { attachmentId: 'att-anterior', tipo: 'consolidado', filename: 'c.pdf', sha256: 'h-ant' },
    incompleto: false,
    documentosFaltantes: [],
    avisosCascada: [],
    regenerado: true,
    ...overrides,
  };
}

const FALLO = respuesta({
  regenerado: false,
  avisosCascada: ['consolidado: adjunto_no_disponible'],
});

beforeEach(() => {
  vi.clearAllMocks();
  mocks.fetchAttachmentPreviewUrl.mockRejectedValue(new Error('preview_url_empty'));
  mocks.downloadAttachment.mockResolvedValue({
    blob: new Blob(['%PDF'], { type: 'application/pdf' }),
    filename: 'c.pdf',
    mimetype: 'application/pdf',
  });
  vi.stubGlobal(
    'URL',
    Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:x'), revokeObjectURL: vi.fn() }),
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function renderVisor(v: ConsolidadoVigencia | null = vigencia({ estado: 'desactualizado' })) {
  return render(
    <ExpedienteVisor instanceId={INSTANCE} attachments={[CONSOLIDADO]} consolidadoWizard={v} />,
  );
}

async function abrir(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Ver expediente consolidado (PDF)' }));
}

describe('hu12799 AC1 — aviso visible al abrir el expediente con fallo', () => {
  it('muestra el aviso junto al indicador gris con la fecha del PDF conservado', async () => {
    mocks.entregarConsolidado.mockResolvedValue(FALLO);
    const user = userEvent.setup();
    renderVisor();

    await abrir(user);

    const aviso = await screen.findByTestId('aviso-fallo-regeneracion');
    expect(aviso).toHaveAttribute('role', 'alert');
    expect(aviso).toHaveTextContent('No se pudo regenerar el consolidado.');
    expect(aviso).toHaveTextContent('no es el más reciente');
    expect(aviso).toHaveTextContent('20/09/2026 10:05 (hora Colombia)');
    // Causa traducida, sin el código crudo.
    expect(aviso).toHaveTextContent(/no está disponible en el almacenamiento/i);
    expect(aviso.textContent).not.toMatch(/adjunto_no_disponible/);
    // Junto al indicador, que sigue en gris (desactualizado) y fuera de su región etiquetada.
    const indicador = screen.getByTestId('vigencia-consolidado');
    expect(indicador).toContainElement(aviso);
    expect(indicador).toHaveAttribute('data-estado', 'desactualizado');
    expect(screen.getByRole('group', { name: /consolidado: desactualizado/i })).not.toContainElement(aviso);
    // Se sirvió el PDF anterior (sigue siendo apertura exitosa).
    await waitFor(() => expect(mocks.openObjectUrlInWindow).toHaveBeenCalledTimes(1));
  });

  it('un consolidado vigente que falla al regenerarse pasa a gris (no queda verde)', async () => {
    mocks.generarConsolidado.mockResolvedValue(FALLO);
    const user = userEvent.setup();
    renderVisor(vigencia({ estado: 'vigente' }));

    await user.click(screen.getByRole('button', { name: 'Re-generar expediente consolidado' }));

    await screen.findByTestId('aviso-fallo-regeneracion');
    expect(screen.getByTestId('vigencia-consolidado')).toHaveAttribute('data-estado', 'desactualizado');
    expect(screen.getByTestId('vigencia-consolidado')).toHaveTextContent('Última generación: 20/09/2026 10:05');
  });

  it('no dice «Expediente consolidado generado» cuando regenerado === false (mensaje contradictorio)', async () => {
    mocks.generarConsolidado.mockResolvedValue(
      respuesta({
        regenerado: false,
        avisosCascada: ['consolidado: excepcion', 'impronta: provider_unavailable'],
      }),
    );
    const user = userEvent.setup();
    renderVisor(vigencia({ estado: 'vigente' }));

    await user.click(screen.getByRole('button', { name: 'Re-generar expediente consolidado' }));

    await screen.findByTestId('aviso-fallo-regeneracion');
    expect(document.body.textContent).not.toMatch(/Expediente consolidado generado/);
    // El fallo del consolidado no se duplica en la caja de error; el de la impronta sí se conserva.
    expect(document.body.textContent).not.toMatch(/No se pudo generar Consolidado/i);
    const alertas = screen.getAllByRole('alert');
    expect(alertas).toHaveLength(2);
    expect(alertas.some((a) => /No se pudo generar .*proveedor no está disponible/i.test(a.textContent ?? ''))).toBe(
      true,
    );
  });

  it('en la apertura con reconstrucción, un fallo no anuncia «se cargó la nueva versión»', async () => {
    mocks.entregarConsolidado.mockResolvedValue(FALLO);
    const user = userEvent.setup();
    renderVisor();

    await abrir(user);

    await screen.findByTestId('aviso-fallo-regeneracion');
    expect(screen.queryByTestId('consolidado-actualizado')).toBeNull();
    expect(screen.queryByTestId('consolidado-reconstruccion')).toBeNull();
  });
});

describe('hu12799 AC2 — reintento desde el aviso', () => {
  it('«Reintentar» lanza una nueva generación (force) y, con éxito, el aviso desaparece y el indicador pasa a verde', async () => {
    mocks.entregarConsolidado.mockResolvedValueOnce(FALLO);
    mocks.generarConsolidado.mockResolvedValueOnce(respuesta());
    const user = userEvent.setup();
    renderVisor();

    await abrir(user);
    const aviso = await screen.findByTestId('aviso-fallo-regeneracion');

    await user.click(within(aviso).getByRole('button', { name: 'Reintentar la regeneración del consolidado' }));

    await waitFor(() => expect(screen.queryByTestId('aviso-fallo-regeneracion')).toBeNull());
    expect(mocks.generarConsolidado).toHaveBeenCalledTimes(1);
    expect(mocks.generarConsolidado).toHaveBeenLastCalledWith(INSTANCE, undefined, true);
    expect(screen.getByTestId('vigencia-consolidado')).toHaveAttribute('data-estado', 'vigente');
  });

  it('si el reintento vuelve a fallar, el aviso sigue y el indicador sigue gris', async () => {
    mocks.entregarConsolidado.mockResolvedValue(FALLO);
    mocks.generarConsolidado.mockResolvedValue(FALLO);
    const user = userEvent.setup();
    renderVisor();

    await abrir(user);
    await user.click(
      within(await screen.findByTestId('aviso-fallo-regeneracion')).getByRole('button', {
        name: /Reintentar/,
      }),
    );

    await waitFor(() => expect(mocks.generarConsolidado).toHaveBeenCalledTimes(1));
    expect(await screen.findByTestId('aviso-fallo-regeneracion')).toBeInTheDocument();
    expect(screen.getByTestId('vigencia-consolidado')).toHaveAttribute('data-estado', 'desactualizado');
  });

  it('doble clic en «Reintentar» respeta el candado: una sola petición', async () => {
    let resolver: (r: GenerarConsolidadoResult) => void = () => undefined;
    mocks.entregarConsolidado.mockResolvedValueOnce(FALLO);
    mocks.generarConsolidado
      
      .mockImplementationOnce(() => new Promise((r) => (resolver = r)));
    const user = userEvent.setup();
    renderVisor();

    await abrir(user);
    const boton = within(await screen.findByTestId('aviso-fallo-regeneracion')).getByRole('button', {
      name: /Reintentar/,
    });
    await user.dblClick(boton);

    expect(mocks.generarConsolidado).toHaveBeenCalledTimes(1);
    expect(boton).toBeDisabled();
    resolver(respuesta());
    await waitFor(() => expect(screen.queryByTestId('aviso-fallo-regeneracion')).toBeNull());
  });
});

describe('hu12799 AC4 — sin fallo no hay aviso', () => {
  it('al montar (sin respuesta aún) no hay aviso de regeneración', () => {
    renderVisor();
    expect(screen.queryByTestId('aviso-fallo-regeneracion')).toBeNull();
  });

  it('apertura exitosa: sin aviso y el mensaje de avisos de cascada conserva «generado»', async () => {
    mocks.entregarConsolidado.mockResolvedValue(
      respuesta({ regenerado: true, avisosCascada: ['impronta: provider_unavailable'] }),
    );
    const user = userEvent.setup();
    renderVisor();

    await abrir(user);

    const alerta = await screen.findByRole('alert');
    expect(alerta).toHaveTextContent(/Expediente consolidado generado/);
    expect(screen.queryByTestId('aviso-fallo-regeneracion')).toBeNull();
  });

  it('vigente reutilizado (regenerado=false sin avisos) no es fallo', async () => {
    mocks.entregarConsolidado.mockResolvedValue(respuesta({ regenerado: false }));
    const user = userEvent.setup();
    renderVisor(vigencia({ estado: 'vigente' }));

    await abrir(user);

    await waitFor(() => expect(mocks.openObjectUrlInWindow).toHaveBeenCalledTimes(1));
    expect(screen.queryByTestId('aviso-fallo-regeneracion')).toBeNull();
    expect(screen.getByTestId('vigencia-consolidado')).toHaveAttribute('data-estado', 'vigente');
  });

  it('fallo SIN PDF anterior (error HTTP) muestra el error traducido y no pinta el aviso', async () => {
    mocks.entregarConsolidado.mockRejectedValue(new Error('storage_unavailable'));
    const user = userEvent.setup();
    renderVisor(vigencia({ estado: 'inexistente', generadoEn: null }));

    await abrir(user);

    // Security B2: la causa se traduce; el código crudo del backend no se pinta.
    const alerta = await screen.findByRole('alert');
    expect(alerta).toHaveTextContent('el almacenamiento de documentos no respondió');
    expect(alerta.textContent).not.toMatch(/storage_unavailable/);
    expect(screen.queryByTestId('aviso-fallo-regeneracion')).toBeNull();
  });

  it('backend sin vigencia: el aviso de fallo se pinta igual, sin fecha inventada', async () => {
    mocks.entregarConsolidado.mockResolvedValue(FALLO);
    const user = userEvent.setup();
    renderVisor(null);

    await abrir(user);

    const aviso = await screen.findByTestId('aviso-fallo-regeneracion');
    expect(aviso).toHaveTextContent('es la última versión disponible');
    expect(screen.queryByTestId('vigencia-consolidado')).toBeNull();
  });
});
