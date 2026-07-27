'use client';

import { useCallback, useState } from 'react';
import { ChevronRight, FileText } from 'lucide-react';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { ActiveDeed } from '@/lib/api/types/procedure-runtime';

/**
 * Umbral de vigencia (días restantes) → tono semántico del badge (HU #10906).
 * ≤ 7 días: rojo (danger) · ≤ 30 días: ámbar (warning) · resto: verde (success).
 */
export function deedVigenciaTone(diasRestantes: number): StatusTone {
  if (diasRestantes <= 7) return 'danger';
  if (diasRestantes <= 30) return 'warning';
  return 'success';
}

/** Etiqueta del badge de vigencia por días restantes. */
export function deedVigenciaLabel(diasRestantes: number): string {
  if (diasRestantes <= 0) return 'Vence hoy';
  if (diasRestantes === 1) return '1 día';
  return `${diasRestantes} días`;
}

/**
 * Collapse de escrituras vigentes de la compañía en el PRIMER paso del wizard (HU #10906).
 * Contraído por defecto; carga perezosa al abrir (`GET /api/v1/tramites/deeds/active`, tenant-scoped
 * por el header). Cada fila proyecta NIT + razón social + días restantes de vigencia con un badge de
 * estado coloreado por umbral. Mismo patrón de disclosure lazy que la bitácora de BiometricStep.
 */
export function ActiveDeedsCollapse({ tenantId }: { tenantId?: string }) {
  const [open, setOpen] = useState(false);
  const [deeds, setDeeds] = useState<ActiveDeed[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadDeeds = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const items = await tramitesClient.fetchActiveDeeds(tenantId);
      setDeeds(items);
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'No se pudieron cargar las escrituras.',
      );
    } finally {
      setLoading(false);
    }
  }, [tenantId]);

  const toggle = () => {
    const next = !open;
    setOpen(next);
    // Carga perezosa: solo la primera vez que se abre (o si quedó sin datos por un error previo).
    if (next && deeds === null && !loading) void loadDeeds();
  };

  return (
    <section className="rounded-2xl border bg-white dark:bg-[#0B0F14] overflow-hidden">
      <button
        type="button"
        onClick={toggle}
        className="flex w-full items-center gap-2 px-4 py-3 text-left focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        aria-expanded={open}
        aria-controls="active-deeds-panel"
      >
        <ChevronRight
          className={`h-4 w-4 shrink-0 transition-transform ${open ? 'rotate-90' : ''}`}
          style={{ color: '#557EFF' }}
          aria-hidden
        />
        <FileText className="h-4 w-4 shrink-0" style={{ color: '#557EFF' }} aria-hidden />
        <span
          className="text-[11px] font-bold uppercase tracking-wide"
          style={{ color: '#162744' }}
        >
          Escrituras vigentes de la compañía
        </span>
      </button>

      {open && (
        <div id="active-deeds-panel" className="border-t px-4 py-3">
          {loading && (
            <p className="text-xs opacity-70" role="status" aria-live="polite">
              Cargando escrituras…
            </p>
          )}
          {error && (
            <p className="text-xs" style={{ color: '#FF4E00' }} role="alert" aria-live="polite">
              {error}
            </p>
          )}
          {!loading && !error && deeds !== null && deeds.length === 0 && (
            <p className="text-xs opacity-70" role="status">
              La compañía no tiene escrituras vigentes.
            </p>
          )}
          {!loading && !error && deeds !== null && deeds.length > 0 && (
            <ul className="space-y-2" aria-label="Escrituras vigentes">
              {deeds.map((deed) => (
                <li
                  // Llave estable por escritura: el backend devuelve UNA fila por cada par
                  // (escritura × compañía), de modo que una misma compañía (NIT) puede aparecer en
                  // varias filas —una por escritura vigente— (Feature #10929).
                  key={deed.id}
                  className="flex items-center justify-between gap-3 rounded-xl border px-3 py-2"
                >
                  <div className="min-w-0">
                    <p className="truncate text-xs font-semibold" style={{ color: '#162744' }}>
                      {deed.name}
                    </p>
                    <p className="text-[11px] font-mono opacity-60">NIT {deed.nit}</p>
                    {deed.description && (
                      <p className="truncate text-[11px] opacity-70">{deed.description}</p>
                    )}
                  </div>
                  <StatusBadge
                    tone={deedVigenciaTone(deed.diasRestantes)}
                    label={deedVigenciaLabel(deed.diasRestantes)}
                    ariaLabel={`Vigencia: ${deedVigenciaLabel(deed.diasRestantes)} restantes`}
                  />
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </section>
  );
}
