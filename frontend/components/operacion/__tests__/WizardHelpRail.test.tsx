import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  fetchActiveDeeds: vi.fn(),
  fetchDocumentRequirementsPreview: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    fetchActiveDeeds: mocks.fetchActiveDeeds,
    fetchDocumentRequirementsPreview: mocks.fetchDocumentRequirementsPreview,
  },
}));

import { WizardHelpRail } from '@/components/operacion/WizardHelpRail';

beforeEach(() => {
  vi.clearAllMocks();
  mocks.fetchActiveDeeds.mockResolvedValue([
    {
      id: 'deed-1',
      nit: '900123456',
      name: 'TRANSPORTES ANDINOS S.A.S',
      representativeName: 'HÉCTOR COPETE',
      representativeDocumentType: 'CC',
      representativeDocumentNumber: '71654328',
      description: 'Escritura 1234',
      diasRestantes: 90,
    },
  ]);
  mocks.fetchDocumentRequirementsPreview.mockResolvedValue([
    {
      documentTypeId: 'doc-1',
      nombre: 'Factura de venta',
      obligatorio: true,
      descripcion: 'Factura del concesionario.',
    },
  ]);
});

/**
 * El carril lleva `backdrop-filter`, que convierte al elemento en bloque contenedor de sus
 * descendientes `position: fixed`. Un panel renderizado DENTRO del carril queda posicionado
 * respecto al icono y no se ve, aunque su estado diga que está abierto. Estas pruebas fijan el
 * comportamiento observable —cada icono abre su panel con su contenido— para que la separación
 * entre carril y paneles no se deshaga al refactorizar.
 */
describe('WizardHelpRail — carril de consulta del paso 1', () => {
  it('ofrece los dos accesos con nombre accesible', () => {
    render(<WizardHelpRail modalidad="matricula_inicial" />);

    expect(screen.getByRole('button', { name: 'Escrituras vigentes' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Documentos a tener listos' })).toBeInTheDocument();
  });

  it('el icono de escrituras abre su panel y carga el listado de la compañía', async () => {
    const user = userEvent.setup();
    render(<WizardHelpRail modalidad="matricula_inicial" />);

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Escrituras vigentes' }));

    const panel = await screen.findByRole('dialog', {
      name: 'Escrituras vigentes de la compañía',
    });
    expect(await within(panel).findByText('TRANSPORTES ANDINOS S.A.S')).toBeInTheDocument();
    expect(mocks.fetchActiveDeeds).toHaveBeenCalledTimes(1);
  });

  it('el icono de documentos abre su panel y carga la guía del trámite', async () => {
    const user = userEvent.setup();
    render(<WizardHelpRail modalidad="matricula_inicial" transitOfficeId="ot-1" />);

    await user.click(screen.getByRole('button', { name: 'Documentos a tener listos' }));

    const panel = await screen.findByRole('dialog', { name: 'Guía de documentos del trámite' });
    expect(await within(panel).findByText('Factura de venta')).toBeInTheDocument();
    expect(mocks.fetchDocumentRequirementsPreview).toHaveBeenCalledWith(
      'matricula_inicial',
      'ot-1',
    );
  });

  it('los paneles NO cuelgan del carril, que crea bloque contenedor por su backdrop-filter', async () => {
    const user = userEvent.setup();
    render(<WizardHelpRail modalidad="matricula_inicial" />);
    const carril = screen.getByRole('group', { name: 'Consultas del trámite' });

    // jsdom no resuelve el bloque contenedor de CSS, así que la regla se fija sobre el árbol: si
    // un panel `fixed inset-0` cuelga del carril, en el navegador se posiciona respecto al icono
    // (36×36 px) en vez del viewport y el gestor no lo ve.
    for (const acceso of ['Escrituras vigentes', 'Documentos a tener listos']) {
      await user.click(screen.getByRole('button', { name: acceso }));
      const panel = await screen.findByRole('dialog');
      expect(carril.contains(panel)).toBe(false);
      await user.click(screen.getByRole('button', { name: 'Cerrar panel' }));
    }
  });

  it('el panel se cierra y puede reabrirse sin volver a consultar', async () => {
    const user = userEvent.setup();
    render(<WizardHelpRail modalidad="matricula_inicial" />);

    await user.click(screen.getByRole('button', { name: 'Escrituras vigentes' }));
    await screen.findByRole('dialog', { name: 'Escrituras vigentes de la compañía' });

    await user.click(screen.getByRole('button', { name: 'Cerrar panel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Escrituras vigentes' }));
    await screen.findByRole('dialog', { name: 'Escrituras vigentes de la compañía' });
    expect(mocks.fetchActiveDeeds).toHaveBeenCalledTimes(1);
  });
});
