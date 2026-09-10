// HU #12372 — el radicado llega compuesto (FT1-0000012) y el frontend lo muestra, lo exporta y lo
// pide tal cual. No hay lógica de formato aquí: un frontend que «arreglara» el formato por su
// cuenta sería la fuente de una contradicción entre pantallas. Lo que sí es del frontend, y se fija
// aquí, es (a) que los dos exports lo traten como TEXTO con el mismo ancho, y (b) que los dos
// buscadores enseñen el formato en su marcador de posición.
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { tramitesExportFields } from '@/lib/tramites/tramites-table-columns';
import { otProceduresExportFields } from '@/lib/admin/ot-procedures-export';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';
import type { OtClientProcedure } from '@/lib/api/types-ot';
import { TramitesFiltrosBar } from '../TramitesFiltrosBar';

const RADICADO = 'FT1-0000012';

describe('HU #12372 — exports: el radicado es texto con prefijo, no una cantidad', () => {
  it('AC2 — el export del gestor lo escribe tal cual y como cadena', () => {
    const campo = tramitesExportFields(['radicado']).find((c) => c.id === 'radicado');
    expect(campo).toBeDefined();

    const fila = { referenceNumber: RADICADO } as InstanceSummary;
    expect(campo!.value(fila)).toBe(RADICADO);
    expect(campo!.raw(fila)).toBe(RADICADO);
    expect(typeof campo!.raw(fila)).toBe('string');
    expect(campo!.width).toBeGreaterThanOrEqual(RADICADO.length + 2);
  });

  it('AC2 — el export del organismo hace exactamente lo mismo, con el mismo ancho', () => {
    const gestor = tramitesExportFields(['radicado']).find((c) => c.id === 'radicado')!;
    const ot = otProceduresExportFields(['radicado']).find((c) => c.id === 'radicado');
    expect(ot).toBeDefined();

    const fila = { referenceNumber: RADICADO } as OtClientProcedure;
    expect(ot!.value(fila)).toBe(RADICADO);
    expect(ot!.raw(fila)).toBe(RADICADO);
    expect(typeof ot!.raw(fila)).toBe('string');
    // Un archivo con la columna más estrecha que el otro se leería como dos formatos distintos.
    expect(ot!.width).toBe(gestor.width);
  });
});

describe('HU #12372 — el buscador enseña el formato del radicado', () => {
  it('AC3 — el marcador de posición por defecto (listado del gestor) muestra FT1-0000012', () => {
    render(
      <TramitesFiltrosBar
        rangoSobre="created"
        onRangoSobreChange={vi.fn()}
        periodo=""
        onPeriodoChange={vi.fn()}
        rangoPropioDesde=""
        rangoPropioHasta=""
        onRangoPropioDesdeChange={vi.fn()}
        onRangoPropioHastaChange={vi.fn()}
        queryFields={[]}
        draftCondiciones={[]}
        onDraftCondicionesChange={vi.fn()}
        condicionesCount={0}
        filtrosTestIdPrefix="hu12372"
        search=""
        onSearchChange={vi.fn()}
        onAplicar={vi.fn()}
        onEmpezarDeCero={vi.fn()}
        columnSelector={null}
      />,
    );

    const caja = screen.getByPlaceholderText(/FT1-0000012/);
    expect(caja).toBeInTheDocument();
    // Y no promete un formato viejo ni el número pelado como único modo.
    expect(caja.getAttribute('placeholder')).not.toMatch(/TRM-/);
  });
});
