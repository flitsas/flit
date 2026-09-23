/**
 * HU #12792 (Épica #12760) — indicador de vigencia en el `ExpedienteVisor` (variante completa) y
 * no regresión de la marca de agua (AC4, decisión D3 / HU #10858).
 *
 * Uso de ejemplo:
 *   <ExpedienteVisor instanceId="inst-1" attachments={[]} consolidadoWizard={detalle.consolidadoWizard} />
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type {
  ConsolidadoVigencia,
  GenerarConsolidadoResult,
  InstanceStatus,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  generarConsolidado: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  openLoadingDocumentTab: vi.fn(() => ({}) as Window),
  openObjectUrlInWindow: vi.fn(),
  showDocumentTabError: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    generarConsolidado: mocks.generarConsolidado,
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

const INSTANCE = 'inst-12792';
const CONSOLIDADO: ProcedureAttachment = {
  id: 'att-cons',
  tipo: 'consolidado',
  filename: 'consolidado.pdf',
  mimetype: 'application/pdf',
} as ProcedureAttachment;

function vigencia(overrides: Partial<ConsolidadoVigencia> = {}): ConsolidadoVigencia {
  return {
    estado: 'vigente',
    generadoEn: '2026-09-23T15:05:00Z',
    origen: 'system',
    definitivo: false,
    modo: null,
    ...overrides,
  };
}

function resultado(): GenerarConsolidadoResult {
  return {
    document: { attachmentId: 'att-cons', tipo: 'consolidado', filename: 'c.pdf', sha256: 'h' },
    incompleto: false,
    documentosFaltantes: [],
    avisosCascada: [],
  };
}

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

describe('HU #12792 — indicador en el ExpedienteVisor (variante completa)', () => {
  it('AC1 — vigente: verde con fecha y hora de la última generación', () => {
    render(
      <ExpedienteVisor instanceId={INSTANCE} attachments={[CONSOLIDADO]} consolidadoWizard={vigencia()} />,
    );
    const ind = screen.getByTestId('vigencia-consolidado');
    expect(ind).toHaveAttribute('data-variante', 'completa');
    expect(ind).toHaveAttribute('data-estado', 'vigente');
    expect(ind).toHaveTextContent('Generado el 23/09/2026 10:05');
    expect(screen.getByTestId('vigencia-consolidado-punto')).toHaveStyle({ background: '#70CF3A' });
  });

  it('AC1 — estado final (definitivo): vigente + «Definitivo»', () => {
    render(
      <ExpedienteVisor
        instanceId={INSTANCE}
        attachments={[CONSOLIDADO]}
        status="aprobado"
        consolidadoWizard={vigencia({ definitivo: true, modo: 'definitivo_estado_final' })}
      />,
    );
    expect(screen.getByTestId('vigencia-consolidado-definitivo')).toHaveTextContent('Definitivo');
  });

  it('AC2 — desactualizado: gris con leyenda de pendiente de regenerar', () => {
    render(
      <ExpedienteVisor
        instanceId={INSTANCE}
        attachments={[CONSOLIDADO]}
        consolidadoWizard={vigencia({ estado: 'desactualizado' })}
      />,
    );
    expect(screen.getByTestId('vigencia-consolidado')).toHaveTextContent('Pendiente de regenerar');
    expect(screen.getByTestId('vigencia-consolidado-punto')).toHaveStyle({ background: '#59677D' });
  });

  it('AC3 — inexistente: aún no se ha generado, sin fecha', () => {
    render(
      <ExpedienteVisor
        instanceId={INSTANCE}
        attachments={[]}
        consolidadoWizard={vigencia({ estado: 'inexistente', generadoEn: null, origen: null })}
      />,
    );
    const ind = screen.getByTestId('vigencia-consolidado');
    expect(ind).toHaveTextContent('Aún no se ha generado');
    expect(ind.textContent).not.toMatch(/\d{2}\/\d{2}\/\d{4}/);
  });

  it('AC5 — el estado se anuncia como status con aria-label textual', () => {
    render(
      <ExpedienteVisor
        instanceId={INSTANCE}
        attachments={[CONSOLIDADO]}
        consolidadoWizard={vigencia({ estado: 'desactualizado' })}
      />,
    );
    expect(
      screen.getByRole('status', { name: /consolidado: desactualizado, pendiente de regenerar/i }),
    ).toBeInTheDocument();
  });

  it.each([undefined, null])('backend sin el campo (%s): no hay indicador', (valor) => {
    render(
      <ExpedienteVisor instanceId={INSTANCE} attachments={[CONSOLIDADO]} consolidadoWizard={valor} />,
    );
    expect(screen.queryByTestId('vigencia-consolidado')).toBeNull();
    // El resto del visor sigue igual.
    expect(screen.getByRole('button', { name: 'Ver expediente consolidado (PDF)' })).toBeInTheDocument();
  });
});

describe('HU #12792 AC4 — sin marca de agua en estados preparado / entregado / aprobado', () => {
  it.each(['preparado', 'entregado', 'aprobado'] as InstanceStatus[])(
    '%s: abrir el consolidado no pide marca de agua (solo instanceId, sin flags)',
    async (status) => {
      mocks.generarConsolidado.mockResolvedValue(resultado());
      const user = userEvent.setup();
      render(
        <ExpedienteVisor
          instanceId={INSTANCE}
          attachments={[CONSOLIDADO]}
          status={status}
          consolidadoWizard={vigencia({ definitivo: status === 'aprobado' })}
        />,
      );

      await user.click(screen.getByRole('button', { name: 'Ver expediente consolidado (PDF)' }));

      await waitFor(() => expect(mocks.openObjectUrlInWindow).toHaveBeenCalledTimes(1));
      expect(mocks.generarConsolidado).toHaveBeenCalledWith(INSTANCE);
      const args = JSON.stringify(mocks.generarConsolidado.mock.calls);
      expect(args).not.toMatch(/watermark|marca/i);
      // El visor no superpone ninguna marca de agua sobre la UI.
      expect(document.body.textContent ?? '').not.toMatch(/marca de agua|watermark|BORRADOR/i);
    },
  );
});
