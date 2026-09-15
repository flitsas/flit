import { describe, it, expect, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';

import { RevocationRequestButton } from '@/components/operacion/RevocationRequestButton';
import type { RevocationEligibility } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12573 (Feature #12565) — "Revocatoria – Botón y gates de permisos".
 *
 * AC1 — rol Administrador + trámite Aprobado/FLIT + sin ventana o dentro de ventana → habilitado.
 * AC2 — rol Operario o interno FLIT → visible pero no accionable.
 * AC3 — ventana configurada y vencida → deshabilitado con motivo "Ventana de revocatoria vencida".
 */

function base64Url(json: unknown): string {
  return btoa(JSON.stringify(json)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** JWT mínimo (sin firma real, `usePermissions` no la verifica) con el rol indicado. */
function setToken(role: string | null): void {
  const header = base64Url({ alg: 'none', typ: 'JWT' });
  const payload = base64Url(role ? { sub: 'user-1', role } : { sub: 'user-1' });
  document.cookie = `flit_token=${header}.${payload}.; path=/`;
}

function clearToken(): void {
  document.cookie = 'flit_token=; path=/; Max-Age=0';
}

const eligibleWithoutWindow: RevocationEligibility = {
  sourceSupported: true,
  windowExpiresAt: null,
  windowExpired: false,
};

const eligibleWithinWindow: RevocationEligibility = {
  sourceSupported: true,
  windowExpiresAt: '2099-01-01T00:00:00Z',
  windowExpired: false,
};

const windowExpired: RevocationEligibility = {
  sourceSupported: true,
  windowExpiresAt: '2020-01-01T00:00:00Z',
  windowExpired: true,
};

const sourceNotSupported: RevocationEligibility = {
  sourceSupported: false,
  windowExpiresAt: null,
  windowExpired: false,
};

afterEach(() => {
  clearToken();
});

describe('RevocationRequestButton — HU #12573', () => {
  it('AC1 — Administrador + Aprobado/FLIT + sin ventana configurada: botón habilitado', () => {
    setToken('AdminCompany');
    render(<RevocationRequestButton instanceId="inst-1" eligibility={eligibleWithoutWindow} />);

    const button = screen.getByRole('button', { name: /Solicitar revocatoria/i });
    expect(button).toBeEnabled();
  });

  it('AC1 — Administrador + Aprobado/FLIT + dentro de la ventana: botón habilitado', () => {
    setToken('AdminCompany');
    render(<RevocationRequestButton instanceId="inst-1" eligibility={eligibleWithinWindow} />);

    expect(screen.getByRole('button', { name: /Solicitar revocatoria/i })).toBeEnabled();
  });

  it('AC2 — rol Operario: botón visible pero no accionable, con motivo de rol', () => {
    setToken('Operario');
    render(<RevocationRequestButton instanceId="inst-1" eligibility={eligibleWithoutWindow} />);

    const button = screen.getByRole('button', { name: /Solicitar revocatoria/i });
    expect(button).toBeVisible();
    expect(button).toBeDisabled();
    expect(
      screen.getByText('Solo el Administrador de la compañía puede solicitar la revocatoria.'),
    ).toBeInTheDocument();
  });

  it('AC2 — interno FLIT (SuperAdmin): botón visible pero no accionable', () => {
    setToken('SuperAdmin');
    render(<RevocationRequestButton instanceId="inst-1" eligibility={eligibleWithoutWindow} />);

    const button = screen.getByRole('button', { name: /Solicitar revocatoria/i });
    expect(button).toBeVisible();
    expect(button).toBeDisabled();
  });

  it('AC3 — ventana vencida: deshabilitado con el motivo "Ventana de revocatoria vencida"', () => {
    setToken('AdminCompany');
    render(<RevocationRequestButton instanceId="inst-1" eligibility={windowExpired} />);

    const button = screen.getByRole('button', { name: /Solicitar revocatoria/i });
    expect(button).toBeDisabled();
    expect(screen.getByText('Ventana de revocatoria vencida')).toBeInTheDocument();
  });

  it('Administrador pero trámite no creado en FLIT (ICT/migrado): deshabilitado con motivo de origen', () => {
    setToken('AdminCompany');
    render(<RevocationRequestButton instanceId="inst-1" eligibility={sourceNotSupported} />);

    const button = screen.getByRole('button', { name: /Solicitar revocatoria/i });
    expect(button).toBeDisabled();
    expect(
      screen.getByText('El trámite no fue creado en FLIT; no admite solicitud de revocatoria.'),
    ).toBeInTheDocument();
  });

  it('sin eligibility (trámite no aprobado / backend viejo): deshabilitado con motivo genérico', () => {
    setToken('AdminCompany');
    render(<RevocationRequestButton instanceId="inst-1" eligibility={null} />);

    const button = screen.getByRole('button', { name: /Solicitar revocatoria/i });
    expect(button).toBeDisabled();
    expect(screen.getByText('La revocatoria no aplica al estado actual del trámite.')).toBeInTheDocument();
  });
});
