/**
 * HU #12774 AC5 — el certificado de Cámara de Comercio se carga en el paso del actor y NO está en el
 * checklist (AC4), pero va en el consolidado: el inventario «Documentos cargados» del cierre tiene
 * que mostrarlo, identificado por el rol de la parte.
 */
import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import ExpedienteVisor, { certificadosDelActor } from '@/components/operacion/ExpedienteVisor';
import type { ChecklistItemView, ProcedureAttachment } from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    downloadAttachment: vi.fn(),
    generarConsolidado: vi.fn(),
  },
}));

function adjunto(tipo: string, id = `att-${tipo}`): ProcedureAttachment {
  return {
    id,
    tipo,
    filename: `${tipo}.pdf`,
    mimetype: 'application/pdf',
    sizeBytes: 1024,
    sha256: 'abc123',
    uploadedAt: '2026-09-23T10:00:00Z',
  } as ProcedureAttachment;
}

const CHECKLIST: ChecklistItemView[] = [
  { key: 'impronta', label: 'Improntas', obligatorio: true, satisfied: true, docTipo: 'impronta' },
];

describe('ExpedienteVisor — certificados de Cámara de Comercio en «Documentos cargados»', () => {
  it('muestra el certificado del comprador identificado por su rol', () => {
    render(
      <ExpedienteVisor
        instanceId="inst-1"
        attachments={[adjunto('impronta'), adjunto('camara_comercio_comprador')]}
        checklist={CHECKLIST}
      />,
    );

    const rejilla = screen.getByLabelText('Documentos del expediente (visor)');
    expect(within(rejilla).getAllByRole('listitem')).toHaveLength(2);
    expect(within(rejilla).getByText(/Cámara de Comercio \(comprador\)/)).toBeInTheDocument();
  });

  it('cada parte tiene su propia ficha', () => {
    const items = certificadosDelActor(
      [adjunto('camara_comercio_vendedor'), adjunto('camara_comercio_comprador')],
      CHECKLIST,
    );

    expect(items.map((i) => i.label)).toEqual([
      'Cámara de Comercio (vendedor)',
      'Cámara de Comercio (comprador)',
    ]);
  });

  it('sin certificado cargado no se agrega ninguna ficha', () => {
    expect(certificadosDelActor([adjunto('impronta')], CHECKLIST)).toEqual([]);
  });

  it('el código histórico sin rol no se trata como certificado de una parte', () => {
    expect(certificadosDelActor([adjunto('camara_comercio')], CHECKLIST)).toEqual([]);
  });
});
