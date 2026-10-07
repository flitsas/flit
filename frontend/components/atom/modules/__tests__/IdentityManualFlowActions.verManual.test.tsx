/**
 * HU13288 — acceso directo «Ver en validaciones manuales» desde el detalle de Identidad (Épica #13202):
 * solo Super Admin, solo proveedor manual y en cualquiera de sus estados manuales.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({ superAdmin: true }));

vi.mock('@/lib/api/client', () => ({ getToken: () => 'token' }));
vi.mock('@/lib/auth/jwt', () => ({
  decodeJwtPayload: () => ({}),
  isSuperAdmin: () => mocks.superAdmin,
  hasPermission: () => false,
}));
vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: { activateManualIdentity: vi.fn(), regenerateManualLink: vi.fn() },
}));

import { IdentityManualFlowActions } from '@/components/atom/modules/IdentityManualFlowActions';
import { enlaceDetalleManual, tieneDetalleManual } from '@/lib/identity/manual-flow';
import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

function validation(overrides: Partial<BiometricValidation> = {}): BiometricValidation {
  return {
    id: 'val-1',
    partyRole: null,
    name: 'Ana Ríos',
    documentType: 'CC',
    documentNumber: '1020304050',
    email: 'ana@example.com',
    status: 'manual_activo',
    intentos: 0,
    maxIntentos: 3,
    score: null,
    expiresAt: '2099-10-06T15:30:00Z',
    validatedAt: null,
    expired: false,
    provider: 'manual',
    captureUrl: null,
    createdAt: '2026-10-01T10:00:00Z',
    ...overrides,
  };
}

const BOTON = 'Ver en validaciones manuales';

beforeEach(() => {
  mocks.superAdmin = true;
});

describe('botón «Ver en validaciones manuales»', () => {
  it.each(['manual_activo', 'pendiente_revision_manual', 'rechazado', 'aprobado'] as const)(
    'Super Admin lo ve con proveedor manual en estado %s',
    (status) => {
      render(
        <IdentityManualFlowActions
          validation={validation({ status, validatedAt: new Date().toISOString() })}
          onChanged={vi.fn()}
          onVerEnManuales={vi.fn()}
        />,
      );
      expect(screen.getByRole('button', { name: BOTON })).toBeInTheDocument();
    },
  );

  it.each(['kyverum', 'mock'] as const)('no aparece con proveedor %s', (provider) => {
    render(
      <IdentityManualFlowActions
        validation={validation({ provider, status: 'aprobado', validatedAt: new Date().toISOString() })}
        onChanged={vi.fn()}
        onVerEnManuales={vi.fn()}
      />,
    );
    expect(screen.queryByRole('button', { name: BOTON })).not.toBeInTheDocument();
  });

  it('no aparece para quien no es Super Admin', () => {
    mocks.superAdmin = false;
    const { container } = render(
      <IdentityManualFlowActions validation={validation()} onChanged={vi.fn()} onVerEnManuales={vi.fn()} />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('no aparece si quien lo monta no ofrece el acceso', () => {
    render(<IdentityManualFlowActions validation={validation()} onChanged={vi.fn()} />);
    expect(screen.queryByRole('button', { name: BOTON })).not.toBeInTheDocument();
  });

  it('al pulsarlo entrega el id de ESA validación', async () => {
    const onVer = vi.fn();
    render(<IdentityManualFlowActions validation={validation({ id: 'abc-123' })} onChanged={vi.fn()} onVerEnManuales={onVer} />);
    await userEvent.setup().click(screen.getByRole('button', { name: BOTON }));
    expect(onVer).toHaveBeenCalledExactlyOnceWith('abc-123');
  });
});

describe('enlace profundo', () => {
  it('construye módulo, pestaña e id (codificado)', () => {
    expect(enlaceDetalleManual('abc-123')).toBe('/?m=validaciones&tab=manuales&manual=abc-123');
    expect(enlaceDetalleManual('a b')).toBe('/?m=validaciones&tab=manuales&manual=a%20b');
  });

  it('tieneDetalleManual excluye proveedores y estados no manuales', () => {
    expect(tieneDetalleManual({ provider: 'manual', status: 'expirado' })).toBe(false);
    expect(tieneDetalleManual({ provider: 'kyverum', status: 'aprobado' })).toBe(false);
  });
});
