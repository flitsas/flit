// HU #13183 (Feature F7 #13119, épica #13090) — el indicador «Firmará» muestra también al mandatario
// asociado de otra compañía, sin exponer datos ajenos. Un bloque por criterio de aceptación.

import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MandatarioFirmaIndicator } from '../MandatarioFirmaIndicator';
import type { MandateSignerPrevisto } from '@/lib/api/types/procedure-runtime';

function previsto(over: Partial<MandateSignerPrevisto> = {}): MandateSignerPrevisto {
  return {
    estado: 'valido',
    nombre: 'Ana Restrepo',
    formaFirma: 'baul',
    modo: 'block',
    ...over,
  };
}

describe('HU #13183 AC1 — muestra al mandatario asociado', () => {
  it('«Firmará: {nombre} / {forma}» de solo lectura con la nota «Mandatario asociado»', () => {
    render(
      <MandatarioFirmaIndicator
        data={previsto({ nivel: 'asociado_de_otra_compania' })}
        loading={false}
      />,
    );
    const indicador = screen.getByTestId('mandatario-firma-valido');
    expect(indicador).toHaveTextContent('Firmará: Ana Restrepo / Baúl de firmas');
    expect(screen.getByTestId('mandatario-firma-asociado')).toHaveTextContent('Mandatario asociado');
    expect(indicador.querySelectorAll('button, input, select, a')).toHaveLength(0);
  });

  it('con biometría muestra la forma de firma correspondiente', () => {
    render(
      <MandatarioFirmaIndicator
        data={previsto({ nivel: 'asociado_de_otra_compania', formaFirma: 'biometria' })}
        loading={false}
      />,
    );
    expect(screen.getByTestId('mandatario-firma-valido')).toHaveTextContent(
      'Firmará: Ana Restrepo / Validación de identidad',
    );
  });
});

describe('HU #13183 AC2 — no expone datos ajenos', () => {
  it('no pinta el nombre de la otra compañía ni el documento aunque la respuesta los trajera', () => {
    const data = {
      ...previsto({ nivel: 'asociado_de_otra_compania' }),
      companiaOrigen: 'OTRA GESTORA SAS',
      documento: '1020304050',
      nit: '900123456',
    } as MandateSignerPrevisto;
    render(<MandatarioFirmaIndicator data={data} loading={false} />);
    const texto = screen.getByTestId('mandatario-firma-valido').textContent ?? '';
    expect(texto).not.toMatch(/OTRA GESTORA|1020304050|900123456/);
  });
});

describe('HU #13183 AC3 — otros niveles sin cambio', () => {
  it.each([
    'explicita',
    'ot_para_compania',
    'propio_de_compania',
    'default_del_ot',
    undefined,
  ] as const)('nivel %s: sin nota de mandatario asociado', (nivel) => {
    render(<MandatarioFirmaIndicator data={previsto({ nivel })} loading={false} />);
    expect(screen.getByTestId('mandatario-firma-valido')).toHaveTextContent(
      'Firmará: Ana Restrepo / Baúl de firmas',
    );
    expect(screen.queryByTestId('mandatario-firma-asociado')).not.toBeInTheDocument();
  });

  it('sin mandatario sigue la alerta de F4-HU6 (modo block)', () => {
    render(
      <MandatarioFirmaIndicator
        data={{ estado: 'sin_mandatario', motivo: 'sin_mandatario_configurado', modo: 'block' }}
        loading={false}
      />,
    );
    expect(screen.getByTestId('mandatario-firma-bloqueo')).toBeInTheDocument();
    expect(screen.queryByTestId('mandatario-firma-asociado')).not.toBeInTheDocument();
  });

  it('cargando y sin datos se comportan igual que antes', () => {
    const { rerender } = render(<MandatarioFirmaIndicator data={null} loading />);
    expect(screen.getByTestId('mandatario-firma-cargando')).toBeInTheDocument();
    rerender(<MandatarioFirmaIndicator data={null} loading={false} />);
    expect(screen.queryByTestId('mandatario-firma-valido')).not.toBeInTheDocument();
  });
});
