'use client';

import { useCallback, useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  InstanceStatus,
  InstanceSummary,
} from '@/lib/api/types/procedure-runtime';

/**
 * Slice M6 — tabla de "Trámites en curso". Lista las instancias del tenant
 * (GET /instances) en el mismo estilo "card rows on solid header" que la grilla
 * maestra. Se refresca al montar y cada vez que cambia `refreshKey` (el padre lo
 * incrementa al volver del wizard tras "Enviar a tránsito").
 */

const estadoChip = (
  estado: InstanceStatus,
): { label: string; bg: string; color: string; border: string } => {
  switch (estado) {
    case 'draft':
      // En preparación — tono ámbar/neutro.
      return {
        label: 'En preparación',
        bg: 'rgba(245,158,11,0.12)',
        color: '#b45309',
        border: 'rgba(245,158,11,0.3)',
      };
    case 'submitted':
      // Enviado a tránsito — tono azul (éxito de envío).
      return {
        label: 'Enviado a tránsito',
        bg: 'rgba(85,126,255,0.12)',
        color: '#557eff',
        border: 'rgba(85,126,255,0.3)',
      };
    default:
      // Cualquier otro estado: titlecase del código (in_review, completed, rejected…).
      return {
        label: titlecase(estado),
        bg: 'rgba(100,116,139,0.12)',
        color: '#475569',
        border: 'rgba(100,116,139,0.3)',
      };
  }
};

function titlecase(value: string): string {
  const text = value.replace(/_/g, ' ');
  return text.charAt(0).toUpperCase() + text.slice(1);
}

function shortDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleDateString('es-CO', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  });
}

function vehiculo(item: InstanceSummary): string {
  const text = `${item.vehiculoMarca ?? ''} ${item.vehiculoLinea ?? ''}`.trim();
  return text || '—';
}

const GRID_COLS = '1.1fr 1.4fr 1.3fr 1.4fr 0.7fr 1.1fr 0.9fr';

interface TramitesTableProps {
  /** Cambia (incrementa) para forzar un refetch — p. ej. al volver del wizard. */
  refreshKey?: number;
}

export function TramitesTable({ refreshKey = 0 }: TramitesTableProps) {
  const [items, setItems] = useState<InstanceSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await tramitesClient.listInstances();
      setItems(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error desconocido');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load, refreshKey]);

  if (loading) {
    return (
      <div
        className="flex flex-col gap-2"
        aria-busy="true"
        aria-label="Cargando trámites"
      >
        {Array.from({ length: 3 }).map((_, i) => (
          <div
            key={i}
            className="h-12 rounded-xl animate-pulse"
            style={{ background: 'rgba(223,229,237,0.5)' }}
          />
        ))}
      </div>
    );
  }

  if (error) {
    return (
      <div
        className="flex flex-col items-center justify-center gap-3 py-10 text-center"
        role="alert"
      >
        <p className="text-sm font-bold">Error al cargar trámites</p>
        <p className="text-xs opacity-60 max-w-xs">{error}</p>
        <button
          onClick={() => void load()}
          className="px-5 py-2.5 rounded-xl text-xs font-semibold border"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
          aria-label="Reintentar cargar trámites"
        >
          Reintentar
        </button>
      </div>
    );
  }

  if (items.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center gap-2 py-10 text-center">
        <p className="text-sm font-bold">Aún no hay trámites</p>
        <p className="text-xs opacity-60 max-w-xs">
          Inicia un trámite con el selector de modalidad de arriba para verlo
          aquí.
        </p>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <div className="min-w-[820px]">
        {/* Header */}
        <div
          className="grid items-center text-[11px] uppercase tracking-wider font-semibold rounded-xl px-4 py-3"
          style={{
            background: '#dfe5ed',
            color: '#162744',
            gridTemplateColumns: GRID_COLS,
          }}
          role="row"
        >
          <div>Placa</div>
          <div>Comprador</div>
          <div>VIN</div>
          <div>Vehículo</div>
          <div>Paso</div>
          <div>Estado</div>
          <div className="text-right">Creado</div>
        </div>

        {/* Rows */}
        <ul className="space-y-2 mt-2" aria-label="Trámites en curso">
          {items.map((item) => {
            const chip = estadoChip(item.estado);
            return (
              <li
                key={item.id}
                className="grid items-center bg-white dark:bg-[#162744] rounded-xl px-4 py-3 text-sm shadow-[0_2px_8px_rgba(22,39,68,0.05)]"
                style={{ gridTemplateColumns: GRID_COLS }}
              >
                <div className="min-w-0">
                  <span className="font-mono font-semibold text-[#162744] dark:text-white truncate block">
                    {item.placa ?? '—'}
                  </span>
                  <span className="text-[10px] font-mono text-[#162744]/50 dark:text-white/50 truncate block">
                    {item.referenceNumber}
                  </span>
                </div>
                <div className="text-[#162744] dark:text-white/90 truncate">
                  {item.compradorNombre ?? '—'}
                </div>
                <div className="font-mono text-xs text-[#162744]/80 dark:text-white/70 truncate">
                  {item.vin ?? '—'}
                </div>
                <div className="text-[#162744]/90 dark:text-white/80 truncate">
                  {vehiculo(item)}
                </div>
                <div className="font-mono text-xs text-[#162744]/70 dark:text-white/60">
                  {item.pasoActual}/{item.totalPasos}
                </div>
                <div>
                  <span
                    className="text-[11px] font-semibold px-2.5 py-1 rounded-full border whitespace-nowrap"
                    style={{
                      background: chip.bg,
                      color: chip.color,
                      borderColor: chip.border,
                    }}
                  >
                    {chip.label}
                  </span>
                </div>
                <div className="font-mono text-xs text-[#162744]/70 dark:text-white/60 text-right">
                  {shortDate(item.createdAt)}
                </div>
              </li>
            );
          })}
        </ul>
      </div>
    </div>
  );
}
