// HU #13266 — Trámites: indicadores del comprobante cargado (Feature #13261, Épica #12741).
import { act, render, renderHook, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  ChecklistItemView,
  FieldValue,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  getInstance: vi.fn(),
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  listOcrTipos: vi.fn(),
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({ tramitesClient: mocks }));

import {
  COPY_ADJUNTO_BLOQUEADO_FLITO,
  COPY_ADJUNTO_PROTEGIDO_FLITO,
  ETIQUETA_CARGADO_POR_FLITO,
  ETIQUETA_IMPUESTO_PAGADO_FLITO,
  MOTIVO_IMPUESTO_PAGADO_FLITO,
  esAdjuntoDeFlito,
  esImpuestoPagadoPorFlito,
  esOrigenFlito,
  mensajeErrorAdjunto,
  mensajeErrorAdjuntoFlito,
} from '@/lib/tramites/flito';
import { mensajeErrorConsolidadoAmigable } from '@/lib/tramites/errores-consolidado';
import { DocumentSlot } from '@/components/operacion/DocumentChecklist';
import {
  COPY_IMPUESTO_FLITO_CARGANDO,
  ImpuestoDepartamentalCheck,
  useImpuestoPagadoPorFlito,
} from '@/components/operacion/ImpuestoDepartamentalCheck';
import { resetTiposOcrCache, useProcedureDocuments } from '@/hooks/useProcedureDocuments';

const INSTANCIA = '22222222-2222-2222-2222-222222222222';
const DETAIL_SISTEMA = 'Este documento lo genera el sistema y no se puede eliminar.';

function adjunto(over: Partial<ProcedureAttachment> = {}): ProcedureAttachment {
  return {
    id: 'att-1',
    tipo: 'liquidacion_impuesto',
    filename: 'liquidacion.pdf',
    mimetype: 'application/pdf',
    sizeBytes: 2048,
    sha256: 'abc',
    source: 'user',
    uploadedAt: '2026-10-05T10:00:00Z',
    provider: 'flito',
    ...over,
  } as ProcedureAttachment;
}

function item(): ChecklistItemView {
  return {
    key: 'liquidacion_impuesto',
    label: 'Liquidación del impuesto',
    obligatorio: true,
    docTipo: 'liquidacion_impuesto',
    satisfied: true,
  };
}

function campo(over: Partial<FieldValue> = {}): FieldValue {
  return {
    formFieldId: '',
    fieldKey: 'impuesto_departamental_pagado',
    valueText: 'true',
    valueJson: null,
    source: 'flito',
    ...over,
  };
}

/** Error como lo arma `request` del cliente: `message` = detail, `problem` = ProblemDetails. */
function error409(code: string, detail: string): Error {
  return Object.assign(new Error(detail), {
    status: 409,
    problem: { title: 'Conflict', status: 409, detail, error: code },
  });
}

function renderSlot(attachment: ProcedureAttachment | undefined, onPreview = vi.fn()) {
  return render(
    <ul>
      <DocumentSlot
        item={item()}
        attachment={attachment}
        uploading={false}
        analyzing={false}
        deleting={false}
        ocr={undefined}
        onUpload={vi.fn()}
        onRemove={vi.fn()}
        onPreview={onPreview}
      />
    </ul>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  resetTiposOcrCache();
});

describe('HU #13266 — detección centralizada «es de FLITO»', () => {
  it('compara sin distinguir mayúsculas ni espacios de borde', () => {
    expect(esOrigenFlito('flito')).toBe(true);
    expect(esOrigenFlito('FLITO')).toBe(true);
    expect(esOrigenFlito(' Flito ')).toBe(true);
    expect(esOrigenFlito('kyverum')).toBe(false);
    expect(esOrigenFlito(null)).toBe(false);
    expect(esOrigenFlito(undefined)).toBe(false);
  });

  it('adjunto: solo con provider = flito', () => {
    expect(esAdjuntoDeFlito(adjunto())).toBe(true);
    expect(esAdjuntoDeFlito(adjunto({ provider: 'Flito' }))).toBe(true);
    expect(esAdjuntoDeFlito(adjunto({ provider: null }))).toBe(false);
    expect(esAdjuntoDeFlito(null)).toBe(false);
  });

  it('impuesto: marcado true y con source = flito', () => {
    expect(esImpuestoPagadoPorFlito([campo()])).toBe(true);
    expect(esImpuestoPagadoPorFlito([campo({ source: 'FLITO' })])).toBe(true);
    expect(esImpuestoPagadoPorFlito([campo({ source: 'user' })])).toBe(false);
    expect(esImpuestoPagadoPorFlito([campo({ valueText: 'false' })])).toBe(false);
    expect(esImpuestoPagadoPorFlito([campo({ fieldKey: 'soat_pagado' })])).toBe(false);
    expect(esImpuestoPagadoPorFlito([])).toBe(false);
    expect(esImpuestoPagadoPorFlito(undefined)).toBe(false);
  });
});

describe('HU #13266 AC1 — adjunto de FLITO identificado', () => {
  it('muestra la etiqueta y NO ofrece reemplazar ni borrar', () => {
    renderSlot(adjunto());

    expect(screen.getByText(ETIQUETA_CARGADO_POR_FLITO)).toBeInTheDocument();
    expect(ETIQUETA_CARGADO_POR_FLITO).toBe('Comprobante cargado — no se puede reemplazar ni eliminar');
    expect(screen.queryByRole('button', { name: /reemplazar archivo/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /borrar/i })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/subir/i)).not.toBeInTheDocument();
  });

  it('conserva la previsualización', async () => {
    const onPreview = vi.fn();
    renderSlot(adjunto(), onPreview);

    await userEvent.click(screen.getByRole('button', { name: /previsualizar/i }));
    expect(onPreview).toHaveBeenCalledTimes(1);
  });

  it('un adjunto del gestor sigue con reemplazar y borrar, sin etiqueta de FLITO', () => {
    renderSlot(adjunto({ provider: null }));

    expect(screen.queryByText(ETIQUETA_CARGADO_POR_FLITO)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /reemplazar archivo/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /borrar/i })).toBeInTheDocument();
  });
});

describe('HU #13266 AC2 — check de impuesto bloqueado por FLITO', () => {
  it('marcado, deshabilitado y con la etiqueta «Pagado (comprobante cargado)»', () => {
    const onChange = vi.fn();
    render(<ImpuestoDepartamentalCheck estado="flito" checked={false} onChange={onChange} />);

    const check = screen.getByRole('checkbox', { name: /impuesto departamental pagado/i });
    expect(check).toBeChecked();
    expect(check).toBeDisabled();
    expect(screen.getByText(ETIQUETA_IMPUESTO_PAGADO_FLITO)).toBeInTheDocument();
    expect(ETIQUETA_IMPUESTO_PAGADO_FLITO).toBe('Pagado (comprobante cargado)');
  });

  it('sin marca de FLITO el gestor decide', async () => {
    const onChange = vi.fn();
    render(<ImpuestoDepartamentalCheck estado="gestor" checked={false} onChange={onChange} />);

    const check = screen.getByRole('checkbox', { name: /impuesto departamental pagado/i });
    expect(check).toBeEnabled();
    expect(check).not.toBeChecked();
    expect(screen.queryByText(ETIQUETA_IMPUESTO_PAGADO_FLITO)).not.toBeInTheDocument();
    await userEvent.click(check);
    expect(onChange).toHaveBeenCalledWith(true);
  });

  it('cargando: deshabilitado mientras se consulta, con el motivo anunciado', () => {
    render(<ImpuestoDepartamentalCheck estado="cargando" checked={false} onChange={vi.fn()} />);

    const check = screen.getByRole('checkbox', { name: /impuesto departamental pagado/i });
    expect(check).toBeDisabled();
    expect(check).toHaveAccessibleDescription(COPY_IMPUESTO_FLITO_CARGANDO);
  });

  it('error al consultar: el gestor decide (el backend conserva la marca de FLITO)', () => {
    render(<ImpuestoDepartamentalCheck estado="error" checked={false} onChange={vi.fn()} />);

    expect(screen.getByRole('checkbox', { name: /impuesto departamental pagado/i })).toBeEnabled();
  });

  it('el hook lee el source del field_value del detalle del trámite', async () => {
    mocks.getInstance.mockResolvedValue({ fieldValues: [campo()] });
    const { result } = renderHook(() => useImpuestoPagadoPorFlito(INSTANCIA, 'tenant-1'));

    expect(result.current).toBe('cargando');
    await waitFor(() => expect(result.current).toBe('flito'));
    expect(mocks.getInstance).toHaveBeenCalledWith(INSTANCIA, 'tenant-1');
  });

  it('el hook: marca del gestor → gestor; fallo de lectura → error; sin trámite → gestor', async () => {
    mocks.getInstance.mockResolvedValueOnce({ fieldValues: [campo({ source: 'user' })] });
    const gestor = renderHook(() => useImpuestoPagadoPorFlito(INSTANCIA));
    await waitFor(() => expect(gestor.result.current).toBe('gestor'));

    mocks.getInstance.mockRejectedValueOnce(new Error('boom'));
    const fallo = renderHook(() => useImpuestoPagadoPorFlito(INSTANCIA));
    await waitFor(() => expect(fallo.result.current).toBe('error'));

    const inerte = renderHook(() => useImpuestoPagadoPorFlito(null));
    expect(inerte.result.current).toBe('gestor');
  });
});

describe('HU #13266 AC3 — errores de bloqueo comprensibles', () => {
  it('409 adjunto_bloqueado_flito → copy en español, sin código técnico', () => {
    const err = error409('adjunto_bloqueado_flito', 'detalle del backend');
    const msg = mensajeErrorAdjunto(err, 'respaldo');

    expect(msg).toBe(COPY_ADJUNTO_BLOQUEADO_FLITO);
    expect(msg).not.toMatch(/adjunto_bloqueado_flito|409/);
  });

  it('409 adjunto_protegido de un adjunto de FLITO → no dice «lo genera el sistema»', () => {
    const err = error409('adjunto_protegido', DETAIL_SISTEMA);
    const msg = mensajeErrorAdjunto(err, 'respaldo', adjunto());

    expect(msg).toBe(COPY_ADJUNTO_PROTEGIDO_FLITO);
    expect(msg).not.toMatch(/sistema|adjunto_protegido/);
  });

  it('409 adjunto_protegido de un documento del sistema → conserva su mensaje', () => {
    const err = error409('adjunto_protegido', DETAIL_SISTEMA);

    expect(mensajeErrorAdjuntoFlito(err, adjunto({ provider: null }))).toBeNull();
    expect(mensajeErrorAdjunto(err, 'respaldo', adjunto({ provider: null }))).toBe(DETAIL_SISTEMA);
  });

  it('otros errores conservan el mensaje del cliente o el respaldo', () => {
    expect(mensajeErrorAdjunto(new Error('Solo en borrador.'), 'respaldo')).toBe('Solo en borrador.');
    expect(mensajeErrorAdjunto('raro', 'respaldo')).toBe('respaldo');
  });

  it('el mapa compartido de errores también distingue a FLITO', () => {
    const protegido = error409('adjunto_protegido', DETAIL_SISTEMA);
    expect(mensajeErrorConsolidadoAmigable(protegido)).toBe(DETAIL_SISTEMA);
    expect(mensajeErrorConsolidadoAmigable(protegido, undefined, { adjunto: adjunto() })).toBe(
      COPY_ADJUNTO_PROTEGIDO_FLITO,
    );
    expect(mensajeErrorConsolidadoAmigable(error409('adjunto_bloqueado_flito', 'x'))).toBe(
      COPY_ADJUNTO_BLOQUEADO_FLITO,
    );
  });

  describe('useProcedureDocuments', () => {
    beforeEach(() => {
      mocks.getChecklist.mockResolvedValue({ items: [item()], faltanObligatorios: 0, completo: true });
      mocks.getInstance.mockResolvedValue({ fieldValues: [] });
      mocks.listOcrTipos.mockResolvedValue([]);
    });

    it('subida bloqueada por FLITO: mensaje claro y relee los adjuntos', async () => {
      mocks.getAttachments.mockResolvedValue([]);
      mocks.uploadAttachment.mockRejectedValue(
        error409('adjunto_bloqueado_flito', 'Este documento ya fue cargado…'),
      );
      const { result } = renderHook(() => useProcedureDocuments(INSTANCIA));
      await waitFor(() => expect(result.current.state.loading).toBe(false));
      const lecturas = mocks.getAttachments.mock.calls.length;

      let ok = true;
      await act(async () => {
        ok = await result.current.upload('liquidacion_impuesto', new File(['x'], 'l.pdf', { type: 'application/pdf' }));
      });

      expect(ok).toBe(false);
      expect(result.current.state.error).toBe(COPY_ADJUNTO_BLOQUEADO_FLITO);
      await waitFor(() => expect(mocks.getAttachments.mock.calls.length).toBeGreaterThan(lecturas));
    });

    it('borrado de un adjunto de FLITO: mensaje de FLITO, no el del sistema', async () => {
      mocks.getAttachments.mockResolvedValue([adjunto()]);
      mocks.deleteAttachment.mockRejectedValue(error409('adjunto_protegido', DETAIL_SISTEMA));
      const { result } = renderHook(() => useProcedureDocuments(INSTANCIA));
      await waitFor(() => expect(result.current.state.attachments).toHaveLength(1));

      await act(async () => {
        await result.current.remove('att-1');
      });

      expect(result.current.state.error).toBe(COPY_ADJUNTO_PROTEGIDO_FLITO);
    });
  });
});

describe('HU #13266 AC4 — accesibilidad', () => {
  it('el check deshabilitado anuncia su motivo con aria-describedby', () => {
    render(<ImpuestoDepartamentalCheck estado="flito" checked={false} onChange={vi.fn()} />);

    const check = screen.getByRole('checkbox', { name: /impuesto departamental pagado.*pagado \(comprobante cargado\)/i });
    const describedBy = check.getAttribute('aria-describedby');
    expect(describedBy).toBeTruthy();
    expect(document.getElementById(describedBy!)).toHaveTextContent(MOTIVO_IMPUESTO_PAGADO_FLITO);
    expect(check).toHaveAccessibleDescription(MOTIVO_IMPUESTO_PAGADO_FLITO);
    // La región del motivo es viva: el paso de «Verificando…» a «pagado» se anuncia.
    expect(document.getElementById(describedBy!)).toHaveAttribute('aria-live', 'polite');
  });

  it('la etiqueta de FLITO es texto (no solo color) y describe la acción que queda', () => {
    renderSlot(adjunto());

    const ver = screen.getByRole('button', { name: /previsualizar/i });
    expect(ver).toHaveAccessibleDescription(ETIQUETA_CARGADO_POR_FLITO);
    // El icono es decorativo: el lector de pantalla solo lee el texto.
    const etiqueta = screen.getByTestId('etiqueta-cargado-por-flito');
    expect(etiqueta.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
  });

  it('el foco por teclado salta el check deshabilitado y sigue al siguiente control', async () => {
    render(
      <>
        <ImpuestoDepartamentalCheck estado="flito" checked={false} onChange={vi.fn()} />
        <button type="button">Enviar al OT</button>
      </>,
    );
    await userEvent.tab();
    expect(screen.getByRole('button', { name: 'Enviar al OT' })).toHaveFocus();
  });
});
