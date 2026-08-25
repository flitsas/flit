import { describe, expect, it } from 'vitest';
import {
  mapIdentidadToTimelineNodes,
  mapStatusHistoryToTimelineNodes,
} from '@/components/operacion/detalle/timeline-mappers';
import type { BiometricValidation, StatusHistory } from '@/lib/api/types/procedure-runtime';

describe('timeline-mappers', () => {
  it('mapStatusHistoryToTimelineNodes ordena y marca el último hito', () => {
    const history: StatusHistory[] = [
      { fromStatus: null, toStatus: 'borrador', changedAt: '2026-01-01T10:00:00Z', reason: null },
      { fromStatus: 'borrador', toStatus: 'entregado', changedAt: '2026-01-02T10:00:00Z', reason: null },
    ];
    const nodes = mapStatusHistoryToTimelineNodes(history);
    expect(nodes).toHaveLength(2);
    expect(nodes[0]!.label).toBe('Borrador');
    expect(nodes[1]!.isActive).toBe(true);
  });

  it('mapIdentidadToTimelineNodes respeta firma del baúl', () => {
    const nodes = mapIdentidadToTimelineNodes('TRASPASO', [], ['vendedor']);
    expect(nodes).toHaveLength(2);
    const vendedor = nodes.find((n) => n.label.includes('Vendedor'));
    expect(vendedor?.info.extra).toBe('Acreditado por firma del baúl');
    expect(vendedor?.color).toBe('#8CC63F');
  });

  it('mapIdentidadToTimelineNodes usa estado biométrico cuando hay validación', () => {
    const validations: BiometricValidation[] = [
      {
        id: 'v1',
        partyRole: 'comprador',
        name: 'Ana Pérez',
        documentType: 'CC',
        documentNumber: '123',
        email: 'ana@test.com',
        status: 'aprobado',
        intentos: 1,
        maxIntentos: 3,
        score: 0.9,
        expiresAt: '2026-05-01T00:00:00Z',
        validatedAt: '2026-04-01T12:00:00Z',
        expired: false,
        provider: 'kyverum',
        captureUrl: null,
      },
    ];
    const nodes = mapIdentidadToTimelineNodes('MATRICULAS', validations, []);
    expect(nodes).toHaveLength(1);
    expect(nodes[0]!.label).toContain('Aprobado');
    expect(nodes[0]!.info.gestor).toBe('Ana Pérez');
  });
});
