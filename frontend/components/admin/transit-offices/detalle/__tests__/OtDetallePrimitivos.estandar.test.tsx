import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { TABLA_HEADER_BG, TABLA_HEADER_FG } from '@/components/atom/table-styles';
import { OtRejilla, OtRejillaPaginada } from '../OtDetallePrimitivos';

// HU #13919 — la rejilla del detalle del OT se dibuja con el estilo estándar de tablas.
describe('OtRejilla — estilo estándar de tablas (HU #13919)', () => {
  it('cabecera con el gris estándar y sin tarjeta que envuelva la tabla', () => {
    render(
      <OtRejilla
        etiqueta="Actores del trámite"
        columnas={['Documento', 'Nombre']}
        filas={[['CC 1', 'Ana'], ['CC 2', 'Luis']]}
      />,
    );

    const tabla = screen.getByRole('table', { name: 'Actores del trámite' });
    const cabecera = within(tabla).getAllByRole('row')[0];
    expect(cabecera).toHaveStyle({ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG });

    // Sin marco ni fondo blanco alrededor de toda la tabla; la tarjeta es cada fila.
    expect(tabla.className).not.toMatch(/\b(border|bg-white|shadow|overflow-hidden)\b/);
    const filas = within(tabla).getAllByRole('row').slice(1);
    expect(filas).toHaveLength(2);
    filas.forEach((f) => expect(f.className).toMatch(/bg-white/));
  });

  it('OtRejillaPaginada pagina de a 10 y ofrece 10/20/30/50', async () => {
    const user = userEvent.setup();
    sessionStorage.clear();
    render(
      <OtRejillaPaginada
        clave="transit-offices.OtDetallePrimitivos.test"
        etiqueta="Actores"
        columnas={['N']}
        filas={Array.from({ length: 25 }, (_, i) => [`fila ${i + 1}`])}
      />,
    );

    expect(screen.getAllByRole('cell')).toHaveLength(10);
    const selector = screen.getByRole('combobox', { name: 'Filas por página' });
    expect(within(selector).getAllByRole('option').map((o) => o.textContent)).toEqual([
      '10',
      '20',
      '30',
      '50',
    ]);
    await user.selectOptions(selector, '20');
    expect(screen.getAllByRole('cell')).toHaveLength(20);
  });
});
