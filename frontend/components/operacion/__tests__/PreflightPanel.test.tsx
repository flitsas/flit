// HU #10603 — Panel de consultas: RNMC condicionado por OT y diferenciado por actor (comprador/vendedor).
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PreflightPanel, checkRoleSuffix } from '../PreflightPanel';
import type {
  PreflightCheck,
  PreflightSnapshot,
} from '@/lib/api/types/procedure-runtime';

function snap(checks: PreflightCheck[]): PreflightSnapshot {
  return { overall: 'green', checks, createdAt: '2026-07-07T00:00:00Z' };
}

const rnmc = (key: string): PreflightCheck => ({
  key,
  label: 'Medidas correctivas (Policía)',
  status: 'ok',
  source: 'verifik_rnmc',
  message: '',
});

const baseProps = {
  loading: false,
  onRun: vi.fn(),
  riesgoAceptado: false,
  onToggleRiesgo: vi.fn(),
};

describe('checkRoleSuffix', () => {
  it('distingue comprador y vendedor por la clave RNMC', () => {
    expect(checkRoleSuffix('rnmc_comprador_medidas_correctivas')).toBe(' (comprador)');
    expect(checkRoleSuffix('rnmc_vendedor_medidas_correctivas')).toBe(' (vendedor)');
  });

  it('no agrega sufijo a checks que no son RNMC por actor', () => {
    expect(checkRoleSuffix('estado_vehiculo')).toBe('');
    expect(checkRoleSuffix('simit_comprador')).toBe('');
  });
});

describe('PreflightPanel — RNMC condicionado (HU #10603)', () => {
  it('muestra los dos checks RNMC diferenciados por rol', () => {
    render(
      <PreflightPanel
        snapshot={snap([
          rnmc('rnmc_comprador_medidas_correctivas'),
          rnmc('rnmc_vendedor_medidas_correctivas'),
        ])}
        {...baseProps}
      />,
    );
    expect(screen.getByText(/Medidas correctivas.*comprador/)).toBeInTheDocument();
    expect(screen.getByText(/Medidas correctivas.*vendedor/)).toBeInTheDocument();
  });

  it('sin RNMC exigido (OT no lo pide), el panel no muestra ningún check RNMC', () => {
    render(
      <PreflightPanel
        snapshot={snap([
          {
            key: 'estado_vehiculo',
            label: 'Estado del vehículo',
            status: 'ok',
            source: 'verifik',
            message: '',
          },
        ])}
        {...baseProps}
      />,
    );
    expect(screen.queryByText(/Medidas correctivas/)).not.toBeInTheDocument();
  });
});

describe('PreflightPanel — trámite sin consultas por migración', () => {
  it('pide la consulta de forma destacada, en vez de la nota genérica', () => {
    render(<PreflightPanel snapshot={null} {...baseProps} esMigrado />);

    expect(screen.getByText('Consulta pendiente')).toBeInTheDocument();
    expect(screen.getByText(/estado actual del vehículo/)).toBeInTheDocument();
    // El genérico se reemplaza, no se acompaña: dos mensajes para lo mismo confunden.
    expect(screen.queryByText(/Ejecuta la consulta para ver el semáforo/)).not.toBeInTheDocument();
  });

  it('no le cuenta al operador que el trámite viene de una migración', () => {
    // El aviso pide la acción, no la justifica: nombrar la migración o la caducidad de las consultas
    // no le cambia nada a quien opera y siembra la duda de si el expediente llegó incompleto.
    const { container } = render(<PreflightPanel snapshot={null} {...baseProps} esMigrado />);

    expect(container.textContent).not.toMatch(/migra|V1|sistema anterior|caduca/i);
  });

  it('en un trámite nativo conserva la nota de siempre', () => {
    render(<PreflightPanel snapshot={null} {...baseProps} />);

    expect(screen.getByText(/Ejecuta la consulta para ver el semáforo/)).toBeInTheDocument();
    expect(screen.queryByText('Consulta pendiente')).not.toBeInTheDocument();
  });

  it('una vez consultado, el aviso desaparece: ya hay semáforo del día', () => {
    render(
      <PreflightPanel
        snapshot={snap([rnmc('rnmc_comprador_medidas_correctivas')])}
        {...baseProps}
        esMigrado
      />,
    );

    expect(screen.queryByText('Consulta pendiente')).not.toBeInTheDocument();
  });
});

// HU #10763 — advertencias y consultas omitidas visibles; el amarillo no ofrece asumir riesgo.
describe('PreflightPanel — fuentes de los proveedores de FEATURE 05 (HU #10763)', () => {
  it('kyverum_fines se presenta como SIMIT y nunca filtra el nombre del proveedor', () => {
    render(
      <PreflightPanel
        snapshot={snap([
          {
            key: 'comparendos',
            label: 'Comparendos',
            status: 'ok',
            source: 'kyverum_fines',
            message: 'Sin comparendos pendientes',
          },
        ])}
        {...baseProps}
      />,
    );
    expect(screen.getByText('SIMIT')).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/KYVERUM/i);
  });

  it('flit_fines se presenta como la fuente interna "Comparendos FLIT"', () => {
    render(
      <PreflightPanel
        snapshot={snap([
          {
            key: 'comparendos',
            label: 'Comparendos',
            status: 'ok',
            source: 'flit_fines',
            message: 'Sin comparendos pendientes',
          },
        ])}
        {...baseProps}
      />,
    );
    expect(screen.getByText('Comparendos FLIT')).toBeInTheDocument();
  });

  it('una consulta omitida por configuración del OT se pinta con su mensaje y fuente FLIT', () => {
    render(
      <PreflightPanel
        snapshot={snap([
          {
            key: 'rnmc_omitida',
            label: 'Medidas correctivas (Policía)',
            status: 'unknown',
            source: 'system',
            message: 'El organismo de tránsito no exige esta consulta.',
          },
        ])}
        {...baseProps}
      />,
    );
    expect(
      screen.getByText('El organismo de tránsito no exige esta consulta.'),
    ).toBeInTheDocument();
    expect(screen.getByText('FLIT')).toBeInTheDocument();
    expect(screen.getByText('unknown')).toBeInTheDocument();
  });
});

describe('PreflightPanel — resumen de advertencias en amarillo (HU #10763)', () => {
  const warnSnap: PreflightSnapshot = {
    overall: 'yellow',
    createdAt: '2026-07-07T00:00:00Z',
    checks: [
      {
        key: 'comparendos',
        label: 'Comparendos',
        status: 'warn',
        source: 'kyverum_fines',
        message: '2 comparendos pendientes de pago.',
      },
      {
        key: 'soat',
        label: 'SOAT',
        status: 'ok',
        source: 'verifik',
        message: 'Vigente',
      },
    ],
  };

  it('lista los hallazgos warn y aclara que se puede continuar', () => {
    render(<PreflightPanel snapshot={warnSnap} {...baseProps} />);
    const franja = screen.getByText('Advertencias del pre-vuelo').closest('div')!;
    expect(franja).toHaveAttribute('role', 'status');
    expect(franja).toHaveTextContent('Comparendos');
    expect(franja).toHaveTextContent('2 comparendos pendientes de pago.');
    expect(franja).toHaveTextContent(/Puedes continuar con el trámite/);
    // El check en verde no es un hallazgo: no se resume.
    expect(franja).not.toHaveTextContent('SOAT');
  });

  // Corazón de la HU: en amarillo no hay bloqueo que levantar, así que no se pide asumir riesgo.
  it('NO ofrece asumir el riesgo cuando el pre-vuelo está en amarillo', () => {
    render(<PreflightPanel snapshot={warnSnap} {...baseProps} />);
    expect(screen.queryByText(/Asumo el riesgo/)).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  });

  it('en rojo sigue ofreciendo asumir el riesgo (sin regresión)', () => {
    render(
      <PreflightPanel
        snapshot={{
          overall: 'red',
          createdAt: '2026-07-07T00:00:00Z',
          checks: [
            {
              key: 'soat',
              label: 'SOAT',
              status: 'fail',
              source: 'verifik',
              message: 'SOAT vencido',
            },
          ],
        }}
        {...baseProps}
      />,
    );
    expect(screen.getByText(/Asumo el riesgo/)).toBeInTheDocument();
    expect(screen.getByRole('checkbox')).toBeInTheDocument();
  });

  it('no muestra la franja de advertencias cuando el pre-vuelo está en verde', () => {
    render(<PreflightPanel snapshot={snap([])} {...baseProps} />);
    expect(screen.queryByText('Advertencias del pre-vuelo')).not.toBeInTheDocument();
  });

  it('lista el detalle de cada comparendo bajo la advertencia de multas', () => {
    render(
      <PreflightPanel
        snapshot={{
          overall: 'yellow',
          createdAt: '2026-07-07T00:00:00Z',
          checks: [
            {
              key: 'simit_comprador_multas',
              label: 'Multas SIMIT',
              status: 'warn',
              source: 'verifik_simit',
              message: '2 multa(s) pendiente(s) por $544.730 COP',
              details: [
                {
                  numero: '25612001000012662173',
                  fecha: '2024-05-01',
                  valor: 344730,
                  organismo: 'STRIA TTOyTTE MCPAL SABANETA',
                  estado: 'Pendiente',
                  infraccion: 'Semáforo en rojo',
                },
                {
                  numero: '25612001000099999999',
                  valor: 200000,
                  estado: 'Pendiente',
                },
              ],
            },
          ],
        }}
        {...baseProps}
      />,
    );
    const detalle = screen.getByRole('list', { name: 'Detalle de comparendos' });
    expect(detalle).toHaveTextContent('Comparendo 25612001000012662173');
    expect(detalle).toHaveTextContent('Semáforo en rojo');
    expect(detalle).toHaveTextContent('STRIA TTOyTTE MCPAL SABANETA');
    expect(detalle).toHaveTextContent('2024-05-01');
    // Formateo COP de ambos valores.
    expect(detalle).toHaveTextContent('$344.730 COP');
    expect(detalle).toHaveTextContent('$200.000 COP');
  });
});

// HU #10885 (Feature #10862, CF-04) — AC1 precarga con origen+fecha, AC2 "Actualizar" con forceRefresh.
describe('PreflightPanel — precarga con origen/fecha y forzar actualización (HU #10885)', () => {
  const checkVehiculo: PreflightCheck = {
    key: 'estado_vehiculo',
    label: 'Estado del vehículo',
    status: 'ok',
    source: 'verifik',
    message: 'Vigente',
  };

  it('AC1 — fromCache=true muestra el badge de origen y la fecha de la consulta', () => {
    const snapshot: PreflightSnapshot = {
      overall: 'green',
      checks: [checkVehiculo],
      createdAt: '2026-07-23T10:00:00Z',
      fromCache: true,
      queriedAt: '2026-07-20T08:30:00Z',
    };
    render(<PreflightPanel snapshot={snapshot} {...baseProps} />);

    const badge = screen.getByText('Dato reutilizado').closest('div')!;
    expect(badge).toHaveTextContent('Origen: RUNT');
    expect(badge).toHaveTextContent(/Consultado el/);
  });

  it('sin fromCache no muestra el badge de origen/fecha (sin regresión del semáforo clásico)', () => {
    render(<PreflightPanel snapshot={snap([checkVehiculo])} {...baseProps} />);
    expect(screen.queryByText('Dato reutilizado')).not.toBeInTheDocument();
  });

  it('AC2 — al pulsar "Actualizar" (ya hay resultado) invoca onRun con forceRefresh=true', async () => {
    const user = userEvent.setup();
    const onRun = vi.fn();
    const snapshot: PreflightSnapshot = {
      overall: 'green',
      checks: [checkVehiculo],
      createdAt: '2026-07-23T10:00:00Z',
      fromCache: true,
      queriedAt: '2026-07-20T08:30:00Z',
    };
    render(<PreflightPanel snapshot={snapshot} {...baseProps} onRun={onRun} />);

    await user.click(screen.getByRole('button', { name: 'Actualizar consulta' }));
    expect(onRun).toHaveBeenCalledWith(true);
  });

  it('la primera consulta (sin resultado previo) invoca onRun con forceRefresh=false', async () => {
    const user = userEvent.setup();
    const onRun = vi.fn();
    render(<PreflightPanel snapshot={null} {...baseProps} onRun={onRun} />);

    await user.click(screen.getByRole('button', { name: 'Consultar RUNT y SIMIT' }));
    expect(onRun).toHaveBeenCalledWith(false);
  });
});
