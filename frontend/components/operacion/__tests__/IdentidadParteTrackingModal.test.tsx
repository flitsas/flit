import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { IdentidadParteTrackingModal } from '@/components/operacion/IdentidadParteTrackingModal';

const listBiometricExpediente = vi.fn();
const getBiometricAuditByValidation = vi.fn();

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listBiometricExpediente: (...args: unknown[]) => listBiometricExpediente(...args),
    getBiometricAuditByValidation: (...args: unknown[]) => getBiometricAuditByValidation(...args),
  },
}));

function abrir() {
  return render(
    <IdentidadParteTrackingModal
      open
      instanceId="inst-1"
      parte="comprador"
      rotulo="Comprador"
      onClose={() => undefined}
    />,
  );
}

const VALIDACION_APROBADA = {
  id: 'val-1',
  partyRole: 'comprador',
  provider: 'kyverum',
  status: 'aprobado',
  name: 'Laura Restrepo Ossa',
  documentType: 'CC',
  documentNumber: '1020998455',
  email: 'l.restrepo@correo.com',
  createdAt: '2026-08-27T11:42:00Z',
  expired: false,
};

describe('IdentidadParteTrackingModal', () => {
  beforeEach(() => {
    listBiometricExpediente.mockReset();
    getBiometricAuditByValidation.mockReset();
    getBiometricAuditByValidation.mockResolvedValue({
      events: [
        {
          occurredAt: '2026-08-27T11:42:00Z',
          stage: 'send',
          outcome: 'ok',
          httpStatus: 200,
          signaturePresent: null,
          secretPresent: null,
          decryptOk: null,
          providerStatus: null,
          errorType: null,
          message: null,
        },
        {
          occurredAt: '2026-08-27T11:51:00Z',
          stage: 'webhook_applied',
          outcome: 'aprobado',
          httpStatus: 200,
          signaturePresent: true,
          secretPresent: true,
          decryptOk: true,
          providerStatus: 'APPROVED',
          errorType: null,
          message: null,
        },
      ],
      referencedFromOtherProcedure: false,
    });
  });

  it('sin validación pero con baúl lo explica en lenguaje corriente', async () => {
    listBiometricExpediente.mockResolvedValue({
      validations: [],
      firmaBaulPartes: ['comprador'],
      provider: 'mock',
    });
    abrir();

    expect(
      await screen.findByText(/quedó acreditada con su firma electrónica/i),
    ).toBeInTheDocument();
  });

  it('sin validación iniciada lo dice sin hablar de bitácoras', async () => {
    listBiometricExpediente.mockResolvedValue({
      validations: [],
      firmaBaulPartes: [],
      provider: 'kyverum',
    });
    abrir();

    expect(
      await screen.findByText(/Todavía no se ha iniciado la validación de identidad/i),
    ).toBeInTheDocument();
  });

  // ── HU #12186: la lectura humana va delante ──────────────────────────────────────────────

  it('la cabecera dice quién se validó y en qué quedó', async () => {
    listBiometricExpediente.mockResolvedValue({
      validations: [VALIDACION_APROBADA],
      firmaBaulPartes: [],
      provider: 'kyverum',
    });
    abrir();

    const panel = await screen.findByRole('region', {
      name: 'Validación de identidad de Laura Restrepo Ossa',
    });
    expect(within(panel).getByText('Laura Restrepo Ossa')).toBeInTheDocument();
    expect(within(panel).getByText(/CC 1020998455 · comprador/)).toBeInTheDocument();
    // «Identidad aprobada» sale dos veces —la píldora de la cabecera y el hito del historial— y las
    // dos son correctas: la cabecera responde de un vistazo y el hito dice cuándo pasó. Se espera,
    // porque el hito depende de la bitácora y la píldora no: comprobarlo sin esperar cuenta una sola
    // en cuanto la máquina va cargada.
    await waitFor(() =>
      expect(within(panel).getAllByText('Identidad aprobada')).toHaveLength(2),
    );
  });

  it('el historial nombra a la persona y mide su respuesta contra el envío', async () => {
    listBiometricExpediente.mockResolvedValue({
      validations: [VALIDACION_APROBADA],
      firmaBaulPartes: [],
      provider: 'kyverum',
    });
    abrir();

    const historial = await screen.findByRole('list', { name: 'Historial de la validación' });
    expect(within(historial).getByText('Identidad aprobada')).toBeInTheDocument();
    expect(within(historial).getByText('Se envió el enlace')).toBeInTheDocument();
    expect(within(historial).getByText('A l.restrepo@correo.com')).toBeInTheDocument();
  });

  it('un rechazo dice el motivo y la salida', async () => {
    listBiometricExpediente.mockResolvedValue({
      validations: [
        {
          ...VALIDACION_APROBADA,
          status: 'rechazado',
          rejectionReason: 'La foto no coincide con el documento',
        },
      ],
      firmaBaulPartes: [],
      provider: 'kyverum',
    });
    abrir();

    expect(await screen.findByText('Identidad rechazada')).toBeInTheDocument();
    expect(screen.getByText('La foto no coincide con el documento')).toBeInTheDocument();
    expect(screen.getByText(/enlace nuevo/i)).toBeInTheDocument();
  });

  it('no ofrece la bitácora técnica: este panel no está acotado por permiso', async () => {
    // El modal lo abre cualquiera que vea el listado. La bitácora enseña proveedor, códigos HTTP y
    // reintentos de integración: diagnóstico de soporte, no lectura del gestor. Sigue disponible en
    // el expediente y en los drawers de identidad, que es donde soporte la consulta.
    listBiometricExpediente.mockResolvedValue({
      validations: [VALIDACION_APROBADA],
      firmaBaulPartes: [],
      provider: 'kyverum',
    });
    abrir();

    await screen.findByRole('region', {
      name: 'Validación de identidad de Laura Restrepo Ossa',
    });
    expect(screen.queryByRole('button', { name: /Bitácora técnica/i })).toBeNull();
    expect(screen.queryByRole('columnheader', { name: /Cifrado/i })).toBeNull();
  });

  it('la lectura humana no menciona ningún servicio externo', async () => {
    listBiometricExpediente.mockResolvedValue({
      validations: [VALIDACION_APROBADA],
      firmaBaulPartes: [],
      provider: 'kyverum',
    });
    abrir();

    const panel = await screen.findByRole('region', {
      name: 'Validación de identidad de Laura Restrepo Ossa',
    });
    // Se mira solo lo que está desplegado: la bitácora técnica, plegada, sí puede nombrarlos —para
    // eso es—, pero su contenido no se renderiza hasta que alguien la abre.
    const visible = panel.textContent?.toLowerCase() ?? '';
    for (const prohibido of ['kyverum', 'proveedor', 'webhook', 'http', 'cifrado']) {
      expect(visible).not.toContain(prohibido);
    }
  });

  it('si la bitácora falla, el estado y el motivo se siguen leyendo', async () => {
    // El titular sale de la validación misma, no de la auditoría: perderlo por un fallo de red
    // dejaría el panel sin responder la pregunta que se vino a hacer.
    getBiometricAuditByValidation.mockRejectedValue(new Error('sin red'));
    listBiometricExpediente.mockResolvedValue({
      validations: [{ ...VALIDACION_APROBADA, status: 'rechazado', rejectionReason: 'Documento ilegible' }],
      firmaBaulPartes: [],
      provider: 'kyverum',
    });
    abrir();

    expect(await screen.findByText('Identidad rechazada')).toBeInTheDocument();
    expect(screen.getByText('Documento ilegible')).toBeInTheDocument();
  });
});
