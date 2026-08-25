import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';

import { WizardStepTracker } from '@/components/operacion/WizardStepTracker';
import type { WizardStep } from '@/lib/api/types/procedure-runtime';

/**
 * Modo condensado del seguimiento de pasos.
 *
 * El seguimiento vive en la cabecera fija y compite por alto con el formulario. Al bajar se
 * aprieta: marcadores más pequeños, rótulo visible solo en el paso en curso y sin la línea de
 * motivos. Lo que se prueba aquí es que apretarlo **no cuesta información**: los rótulos de los
 * demás pasos siguen anunciándose y el nombre accesible de cada botón queda intacto, así que quien
 * navega por teclado o lector de pantalla no nota diferencia entre los dos modos.
 */
const PASOS: WizardStep[] = [
  { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'complete', reasons: [] },
  { index: 1, key: 'comprador', label: 'Comprador', status: 'incomplete', reasons: ['runt_comprador'] },
  { index: 2, key: 'documentos', label: 'Documentos', status: 'locked', reasons: [] },
];

function pintar(compacto: boolean) {
  render(
    <WizardStepTracker
      steps={PASOS}
      activeIndex={1}
      onGoToStep={vi.fn()}
      compacto={compacto}
    />,
  );
}

describe('WizardStepTracker — modo condensado', () => {
  it('conserva los nombres accesibles de todos los pasos', () => {
    pintar(true);
    // El nombre accesible es el contrato con el teclado y el lector: no cambia entre modos.
    expect(screen.getByRole('button', { name: /^Paso 1: Consulta Vehículo/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Paso 2: Actores/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Paso 3: Requisitos/ })).toBeInTheDocument();
  });

  it('los rótulos de los pasos que no están en curso siguen en el documento, solo dejan de verse', () => {
    pintar(true);
    // `sr-only` no es `display:none`: el texto sigue ahí para quien lo lee con asistencia.
    const rotulo = screen.getByTitle('Consulta Vehículo');
    expect(rotulo).toBeInTheDocument();
    expect(rotulo.className).toContain('sr-only');
    // El del paso en curso sí se ve.
    expect(screen.getByTitle('Actores').className).not.toContain('sr-only');
  });

  it('oculta los motivos del paso incompleto, que es lo que hacía saltar el contenido', () => {
    pintar(true);
    expect(screen.queryByText(/Faltan datos del comprador|runt_comprador/)).toBeNull();
  });

  it('en modo normal se ven todos los rótulos y el motivo del paso en curso', () => {
    pintar(false);
    expect(screen.getByTitle('Consulta Vehículo').className).not.toContain('sr-only');
    expect(screen.getByTitle('Requisitos').className).not.toContain('sr-only');
    // El motivo vuelve: en la cabecera completa hay sitio y es donde el gestor lo espera. Es UNA
    // línea, no una lista de viñetas — la línea de tiempo resume y el detalle vive en el paso.
    expect(screen.getByText('Consulta RUNT del comprador')).toBeInTheDocument();
  });

  it('con varios motivos resume en una sola línea en vez de volcarlos todos', () => {
    // El caso real: el paso de requisitos llegaba con el agregado MÁS un código por cada documento
    // que falta, y la línea de tiempo los pintaba los siete, en crudo y en mayúsculas.
    render(
      <WizardStepTracker
        steps={[
          { index: 0, key: 'consulta', label: 'Consulta', status: 'complete', reasons: [] },
          {
            index: 1,
            key: 'documentos',
            label: 'Documentos',
            status: 'incomplete',
            reasons: [
              'documentos_incompletos',
              'DOCUMENT_COMPRAVENTA_REQUIRED',
              'DOCUMENT_SOAT_REQUIRED',
            ],
          },
        ]}
        activeIndex={1}
        onGoToStep={vi.fn()}
        compacto={false}
      />,
    );

    expect(screen.getByText('Faltan documentos obligatorios y 2 más')).toBeInTheDocument();
    expect(screen.queryByText(/DOCUMENT COMPRAVENTA REQUIRED/)).toBeNull();
    // El detalle no se pierde: queda en el title, al alcance sin ocupar la línea de tiempo.
    expect(
      screen.getByTitle(/Falta el documento: compraventa/),
    ).toBeInTheDocument();
  });
});
