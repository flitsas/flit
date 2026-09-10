/**
 * «Documentos cargados» (paso de resumen): un documento OPCIONAL que falta no puede leerse igual que
 * uno obligatorio que falta.
 *
 * El defecto: `DocRow` solo miraba `satisfied`, así que los dos se pintaban «Pendiente» en ámbar. En
 * el último paso antes de radicar, ese ámbar se lee como deuda y el gestor no tenía cómo distinguir
 * qué bloquea de qué es informativo. `obligatorio` ya venía en el checklist; no se estaba mirando.
 */
import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import ExpedienteVisor from '@/components/operacion/ExpedienteVisor';
import type { ChecklistItemView } from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    downloadAttachment: vi.fn(),
    generarConsolidado: vi.fn(),
  },
}));

function item(over: Partial<ChecklistItemView> & { key: string }): ChecklistItemView {
  return {
    label: over.key,
    obligatorio: true,
    satisfied: false,
    docTipo: over.key,
    ...over,
  };
}

/**
 * La ficha de un documento, por su CÓDIGO — para no confundir badges entre tarjetas de la rejilla.
 * El `title` de la ficha es «Nombre (codigo)» (`catalogDocumentTitle`), así que el código ancla mejor
 * que el nombre: es único y no depende de cómo el catálogo resuelva el rótulo.
 */
function ficha(codigo: string) {
  return screen
    .getByTitle((title) => title.endsWith(`(${codigo})`))
    .closest('li') as HTMLElement;
}

/** La barrita de estado de una ficha (el `div` interno del contenedor decorativo). */
function barra(li: HTMLElement): HTMLElement {
  return li.querySelector('[aria-hidden="true"] > div') as HTMLElement;
}

const CHECKLIST: ChecklistItemView[] = [
  item({ key: 'compraventa', label: 'Compraventa', obligatorio: true, satisfied: false }),
  item({ key: 'paz_salvo_rnmc', label: 'Paz y salvo RNMC', obligatorio: false, satisfied: false }),
  item({ key: 'soat', label: 'SOAT', obligatorio: true, satisfied: true }),
  item({ key: 'cert_tradicion', label: 'Certificado de tradición', obligatorio: false, satisfied: true }),
];

describe('ExpedienteVisor — obligatorio vs opcional en «Documentos cargados»', () => {
  it('el obligatorio que falta sigue diciendo «Pendiente»', () => {
    render(<ExpedienteVisor instanceId="inst-1" attachments={[]} checklist={CHECKLIST} />);
    expect(within(ficha('compraventa')).getByText('Pendiente')).toBeInTheDocument();
  });

  it('el opcional que falta dice «No cargado», no «Pendiente»', () => {
    render(<ExpedienteVisor instanceId="inst-1" attachments={[]} checklist={CHECKLIST} />);
    const opcional = ficha('paz_salvo_rnmc');
    expect(within(opcional).getByText('No cargado')).toBeInTheDocument();
    expect(within(opcional).queryByText('Pendiente')).toBeNull();
  });

  it('lo cargado dice «Cargado», sea obligatorio u opcional', () => {
    render(<ExpedienteVisor instanceId="inst-1" attachments={[]} checklist={CHECKLIST} />);
    expect(within(ficha('soat')).getByText('Validado')).toBeInTheDocument();
    expect(within(ficha('cert_tradicion')).getByText('Validado')).toBeInTheDocument();
  });

  // El inventario no se recorta: ocultar los opcionales que faltan los haría aparecer y desaparecer
  // según su estado, y le quitaría al gestor el único sitio donde ve que falta antes de radicar.
  it('ningún documento se oculta: los cuatro siguen en la rejilla', () => {
    render(<ExpedienteVisor instanceId="inst-1" attachments={[]} checklist={CHECKLIST} />);
    const rejilla = screen.getByLabelText('Documentos del expediente (visor)');
    expect(within(rejilla).getAllByRole('listitem')).toHaveLength(4);
  });

  // El color pesa más que la palabra: si la barra se quedara ámbar, «No cargado» seguiría leyéndose
  // como deuda. Solo el obligatorio que falta lleva el ámbar de advertencia.
  it('solo el obligatorio que falta pinta la barra en ámbar', () => {
    render(<ExpedienteVisor instanceId="inst-1" attachments={[]} checklist={CHECKLIST} />);

    expect(barra(ficha('compraventa')).getAttribute('style')).toContain('badge-warning-fg');
    // El opcional que falta va en gris neutro, no en ámbar.
    expect(barra(ficha('paz_salvo_rnmc')).getAttribute('style')).not.toContain('badge-warning-fg');
    // Y lo cargado, en el verde de marca (`#8CC63F`, que jsdom normaliza a rgb).
    expect(barra(ficha('soat')).getAttribute('style')).toContain('rgb(140, 198, 63)');
  });
});
