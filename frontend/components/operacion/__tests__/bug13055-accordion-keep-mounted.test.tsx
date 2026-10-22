// Bug #13055 — al colapsar "Datos Comerciales" se borraba el valor de venta digitado: el acordeón
// desmontaba `CommercialForm`, cuyo estado sin guardar es local.
//
// Uso de ejemplo:
//   <WizardAccordion title="Datos Comerciales" keepMounted><CommercialForm .../></WizardAccordion>
//   colapsar → el panel queda `hidden`, el formulario sigue montado y conserva lo digitado.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { WizardAccordion } from '../WizardAccordion';

function CampoLocal() {
  const [valor, setValor] = useState('');
  return <input aria-label="Valor de venta" value={valor} onChange={(e) => setValor(e.target.value)} />;
}

function colapsarYExpandir() {
  fireEvent.click(screen.getByRole('button', { name: 'Contraer Datos Comerciales' }));
  fireEvent.click(screen.getByRole('button', { name: 'Expandir Datos Comerciales' }));
}

describe('WizardAccordion keepMounted (Bug #13055)', () => {
  it('conserva lo digitado al colapsar y volver a expandir', () => {
    render(
      <WizardAccordion title="Datos Comerciales" defaultOpen keepMounted>
        <CampoLocal />
      </WizardAccordion>,
    );
    fireEvent.change(screen.getByLabelText('Valor de venta'), { target: { value: '45.000.000' } });

    colapsarYExpandir();

    expect(screen.getByLabelText('Valor de venta')).toHaveValue('45.000.000');
  });

  it('colapsado, el panel sigue montado pero oculto (fuera del árbol accesible)', () => {
    render(
      <WizardAccordion title="Datos Comerciales" defaultOpen keepMounted>
        <CampoLocal />
      </WizardAccordion>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Contraer Datos Comerciales' }));

    expect(screen.queryByRole('region', { name: 'Datos Comerciales' })).toBeNull();
    expect(screen.getByLabelText('Valor de venta', { selector: 'input' })).not.toBeVisible();
  });

  it('sin keepMounted mantiene el comportamiento de siempre: colapsar desmonta el contenido', () => {
    render(
      <WizardAccordion title="Datos Comerciales" defaultOpen>
        <CampoLocal />
      </WizardAccordion>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Contraer Datos Comerciales' }));

    expect(screen.queryByLabelText('Valor de venta')).toBeNull();
  });

  it('el wizard mantiene montados los formularios con ref de guardado (comercial y prenda)', () => {
    const wizard = readFileSync(path.resolve(__dirname, '../TramiteWizard.tsx'), 'utf8');
    const comercial = wizard.slice(wizard.indexOf('title="Datos Comerciales"'), wizard.indexOf('<CommercialForm'));
    expect(comercial).toContain('keepMounted');

    const prendas = wizard.split('title="Asignación de Prenda / Limitación a la Propiedad"').slice(1);
    expect(prendas).toHaveLength(2);
    for (const bloque of prendas) {
      expect(bloque.slice(0, bloque.indexOf('<PrendaForm'))).toContain('keepMounted');
    }
  });
});
