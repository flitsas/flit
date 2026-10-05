/**
 * HU-C5 (#13300) — pestaña «Manuales»: tabla del modelo de trámites, filtros, paginación y estados.
 * Vitest + RTL contra el adaptador simulado (sin latencia, reloj fijo).
 */
import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ValidacionesManuales } from '@/components/atom/modules/ValidacionesManuales';
import { createMockManualReviewClient } from '@/lib/api/manual-review-mock';
import type { ManualReviewClient } from '@/lib/api/manual-review-client';
import { ManualStatusBadge } from '@/components/atom/modules/ManualStatusBadge';
import { formatEspera } from '@/lib/identidad/manual-review-meta';

const NOW = new Date('2026-10-05T15:00:00Z');
const nuevoCliente = (count = 27) => createMockManualReviewClient({ count, delayMs: 0, now: NOW });

describe('Pestaña Manuales — tabla', () => {
  it('muestra las columnas del modelo de trámites y la fecha en DD/MM/YYYY HH:mm', async () => {
    render(<ValidacionesManuales client={nuevoCliente()} />);
    const tabla = await screen.findByRole('table', { name: 'Validaciones manuales' });
    const cabeceras = within(tabla).getAllByRole('columnheader').map((h) => h.textContent);
    expect(cabeceras).toEqual([
      'Nombre',
      'Documento',
      'Compañía',
      'Origen',
      'Estado',
      'Fecha de activación',
      'Tiempo en espera',
      'Acciones',
    ]);
    // Primera fila: activada hace 20 min → 05/10/2026 09:40 (hora de Bogotá), sin segundos.
    expect(within(tabla).getAllByText(/^05\/10\/2026 09:40$/).length).toBe(1);
    expect(within(tabla).getByText('20 min')).toBeInTheDocument();
  });

  it('pagina con «Filas por página» y navegación numerada', async () => {
    const user = userEvent.setup();
    render(<ValidacionesManuales client={nuevoCliente()} />);
    await screen.findByText('Persona de prueba 01');
    expect(screen.queryByText('Persona de prueba 11')).not.toBeInTheDocument();
    expect(screen.getByText(/Filas por página/i)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /^2$|página 2/i }));
    expect(await screen.findByText('Persona de prueba 11')).toBeInTheDocument();
  });

  it('filtra por estado y por origen consultando al cliente', async () => {
    const user = userEvent.setup();
    const client = nuevoCliente();
    const spy = vi.spyOn(client, 'listManual');
    render(<ValidacionesManuales client={client} />);
    await screen.findByText('Persona de prueba 01');

    await user.selectOptions(screen.getByLabelText('Estado'), 'aprobado');
    await waitFor(() => expect(spy.mock.calls.at(-1)![0]).toMatchObject({ status: 'aprobado', page: 1 }));
    const tabla = await screen.findByRole('table', { name: 'Validaciones manuales' });
    await waitFor(() => {
      const chips = within(tabla).getAllByRole('status');
      expect(chips.every((c) => /Aprobada manual/.test(c.textContent ?? ''))).toBe(true);
    });

    await user.selectOptions(screen.getByLabelText('Origen'), 'mandatario');
    await waitFor(() => expect(spy.mock.calls.at(-1)![0]).toMatchObject({ origin: 'mandatario' }));
  });

  it('busca por texto con retardo y muestra el vacío con filtros', async () => {
    const user = userEvent.setup();
    render(<ValidacionesManuales client={nuevoCliente()} />);
    await screen.findByText('Persona de prueba 01');
    await user.type(screen.getByLabelText('Nombre o número de documento'), 'no-existe');
    expect(await screen.findByText(/Ninguna validación manual coincide/)).toBeInTheDocument();
  });

  it('muestra el error con «Reintentar» y recupera la lista', async () => {
    const user = userEvent.setup();
    const base = nuevoCliente();
    let falla = true;
    const client: ManualReviewClient = {
      ...base,
      listManual: (p, s) => (falla ? Promise.reject(new Error('boom')) : base.listManual(p, s)),
    };
    render(<ValidacionesManuales client={client} />);
    expect(await screen.findByText('No se pudieron cargar las validaciones manuales.')).toBeInTheDocument();
    falla = false;
    await user.click(screen.getByRole('button', { name: /Reintentar/ }));
    expect(await screen.findByText('Persona de prueba 01')).toBeInTheDocument();
  });

  it('muestra el vacío sin filtros', async () => {
    render(<ValidacionesManuales client={nuevoCliente(0)} />);
    expect(await screen.findByText(/Aún no hay validaciones manuales/)).toBeInTheDocument();
  });

  it('cada fila tiene la acción «Ver detalle» con nombre accesible', async () => {
    render(<ValidacionesManuales client={nuevoCliente()} />);
    expect(await screen.findByRole('button', { name: 'Ver detalle de Persona de prueba 01' })).toBeInTheDocument();
  });
});

describe('Chips de estado', () => {
  const casos: Array<[string, string, string]> = [
    ['manual_activo', 'Esperando captura', 'info'],
    ['pendiente_revision_manual', 'Pendiente de revisión', 'info'],
    ['aprobado', 'Aprobada manual', 'success'],
    ['rechazado', 'Rechazada', 'danger'],
    ['expirado', 'Vencida', 'neutral'],
  ];
  it.each(casos)('%s → «%s» con tono %s, icono y texto', (status, texto, tono) => {
    const { container } = render(<ManualStatusBadge status={status} />);
    const chip = screen.getByRole('status', { name: `Estado: ${texto}` });
    expect(chip).toHaveTextContent(texto);
    expect(chip.getAttribute('style') ?? '').toContain(`--badge-${tono}-bg`);
    expect(container.querySelector('svg')).not.toBeNull();
  });
});

describe('formatEspera', () => {
  it('formatea minutos, horas y días', () => {
    expect(formatEspera(0)).toBe('< 1 min');
    expect(formatEspera(45)).toBe('45 min');
    expect(formatEspera(125)).toBe('2 h 05 min');
    expect(formatEspera(60 * 28)).toBe('1 d 4 h');
  });
});
