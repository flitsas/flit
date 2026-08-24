'use client';

import { useMemo, useState } from 'react';
import { AlertTriangle, Archive, CheckCircle2, Loader2, Plus, RefreshCw } from 'lucide-react';
import { superadminClient } from '@/lib/api/superadmin-client';
import { familiaLabel } from '@/lib/api/types/familia-labels';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';
import { NuevoTipoTramiteModal } from './NuevoTipoTramiteModal';
import { TipoTramiteBarrera } from './TipoTramiteBarrera';
import { TipoTramiteCapacidades } from './TipoTramiteCapacidades';
import { TipoTramiteDocumentos } from './TipoTramiteDocumentos';
import { TipoTramiteIdentidad } from './TipoTramiteIdentidad';
import { TipoTramiteQuipux } from './TipoTramiteQuipux';
import { TipoTramiteRecorrido } from './TipoTramiteRecorrido';
import { useDetalleTipo, useTiposTramite } from './useTiposTramite';

type Pestana = 'identidad' | 'capacidades' | 'recorrido' | 'documentos' | 'quipux';

const PESTANAS: { id: Pestana; label: string }[] = [
  { id: 'identidad', label: 'Identidad' },
  { id: 'capacidades', label: 'Capacidades' },
  { id: 'recorrido', label: 'Recorrido' },
  { id: 'documentos', label: 'Documentos' },
  // La radicación es una faceta más del tipo, no un catálogo aparte: por eso vive aquí y no en
  // una pantalla de Quipux.
  { id: 'quipux', label: 'Radicación' },
];

/**
 * Configurador de tipos de trámite (ADR-0050).
 *
 * Reúne en una pantalla lo que hasta ahora solo se podía tocar por SQL: la identidad del tipo, sus
 * capacidades, el recorrido del asistente y la matriz documental — y la barrera que decide si el
 * gestor puede elegirlo. Ese era el hueco que dejaba «habilitar un trámite es configuración, no
 * despliegue» a medio cumplir.
 *
 * El listado va a la izquierda porque el trabajo real es comparar: revisar 21 tipos es ir viendo
 * cuál está operable y cuál no, no abrir uno y volver.
 */
export function TiposTramitePanel() {
  const { tipos, cargando, error, recargar, setTipos } = useTiposTramite();
  const [seleccionado, setSeleccionado] = useState<string | null>(null);
  const [pestana, setPestana] = useState<Pestana>('identidad');
  const [filtro, setFiltro] = useState('');
  const [creando, setCreando] = useState(false);
  const [retirando, setRetirando] = useState(false);
  const [confirmarRetiro, setConfirmarRetiro] = useState(false);
  const [errorRetiro, setErrorRetiro] = useState<string | null>(null);

  const tipo = tipos.find((t) => t.id === seleccionado) ?? null;
  const { detalle, cargando: cargandoDetalle, error: errorDetalle, recargar: recargarDetalle } =
    useDetalleTipo(seleccionado);

  const visibles = useMemo(() => {
    const q = filtro.trim().toLowerCase();
    if (!q) return tipos;
    return tipos.filter(
      (t) =>
        t.name.toLowerCase().includes(q) ||
        t.code.toLowerCase().includes(q) ||
        familiaLabel(t.family).toLowerCase().includes(q),
    );
  }, [tipos, filtro]);

  const operables = tipos.filter((t) => t.wizardEnabled).length;

  const aplicar = (actualizado: ProcedureTypeSummary) =>
    setTipos((prev) => prev.map((t) => (t.id === actualizado.id ? actualizado : t)));

  const retirar = async () => {
    if (!tipo) return;
    setRetirando(true);
    setErrorRetiro(null);
    try {
      await superadminClient.retirar(tipo.id);
      setConfirmarRetiro(false);
      setSeleccionado(null);
      await recargar();
    } catch (e: unknown) {
      setErrorRetiro(e instanceof Error ? e.message : 'No se pudo retirar el tipo.');
    } finally {
      setRetirando(false);
    }
  };

  if (cargando) {
    return (
      <p className="flex items-center gap-2 p-6 text-xs opacity-70">
        <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
        Cargando el catálogo de tipos…
      </p>
    );
  }

  if (error) {
    return (
      <div className="flex flex-col items-start gap-3 p-6" role="alert">
        <p className="text-xs" style={{ color: '#C2410C' }}>
          {error}
        </p>
        <button
          type="button"
          onClick={() => void recargar()}
          className="inline-flex items-center gap-1.5 rounded-xl border px-3 py-2 text-xs font-semibold border-[#DFE5ED] dark:border-white/10"
          style={{ color: '#557EFF' }}
        >
          <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
          Reintentar
        </button>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-xs opacity-70">
          {tipos.length} tipos en el catálogo · <strong>{operables} operables</strong> en el asistente
        </p>
        <div className="flex flex-wrap items-center gap-2">
          <input
            type="search"
            value={filtro}
            onChange={(e) => setFiltro(e.target.value)}
            placeholder="Buscar por nombre, código o familia…"
            aria-label="Buscar tipo de trámite"
            className="w-full max-w-xs rounded-xl border px-3 py-2 text-xs border-[#DFE5ED] bg-white text-[#162744] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          />
          <button
            type="button"
            onClick={() => setCreando(true)}
            className="inline-flex shrink-0 items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
            style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
          >
            <Plus className="h-3.5 w-3.5" aria-hidden="true" />
            Nuevo tipo
          </button>
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-[20rem_1fr]">
        <ul className="flex max-h-[34rem] flex-col gap-1 overflow-y-auto" aria-label="Tipos de trámite">
          {visibles.map((t) => {
            const activo = t.id === seleccionado;
            return (
              <li key={t.id}>
                <button
                  type="button"
                  onClick={() => setSeleccionado(t.id)}
                  aria-current={activo ? 'true' : undefined}
                  className="w-full rounded-xl border px-3 py-2.5 text-left transition hover:border-[#557EFF] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
                  style={
                    activo
                      ? { borderColor: '#557EFF', background: 'rgba(85,126,255,0.08)' }
                      : { borderColor: '#DFE5ED' }
                  }
                >
                  <span className="flex items-start justify-between gap-2">
                    <span className="min-w-0">
                      <span className="block text-xs font-semibold text-[#162744] dark:text-white">
                        {t.name}
                      </span>
                      <span className="block text-xs opacity-55">{familiaLabel(t.family)}</span>
                    </span>
                    {t.wizardEnabled ? (
                      <CheckCircle2
                        className="h-3.5 w-3.5 shrink-0"
                        style={{ color: '#0E9F6E' }}
                        aria-label="Operable"
                      />
                    ) : (
                      <span
                        className="mt-1 h-1.5 w-1.5 shrink-0 rounded-full"
                        style={{ background: '#C8D2E0' }}
                        aria-label="No operable"
                      />
                    )}
                  </span>
                </button>
              </li>
            );
          })}
          {visibles.length === 0 && (
            <li className="px-3 py-6 text-xs opacity-60">Ningún tipo coincide con la búsqueda.</li>
          )}
        </ul>

        <div className="rounded-2xl border p-4 border-[#DFE5ED] dark:border-white/10">
          {!tipo ? (
            <p className="py-10 text-center text-xs opacity-60">
              Elige un tipo de la lista para ver y ajustar su configuración.
            </p>
          ) : (
            <div className="flex flex-col gap-4">
              <header className="flex flex-col gap-3 border-b pb-4 border-[#DFE5ED] dark:border-white/10">
                <div>
                  <h2 className="text-base font-semibold text-[#162744] dark:text-white">
                    {tipo.name}
                  </h2>
                  <code className="text-xs opacity-55">{tipo.code}</code>
                </div>
                <TipoTramiteBarrera tipo={tipo} onCambiado={aplicar} />
                <Validacion detalle={detalle} />
                {tipo.publicationStatus !== 'archived' && (
                  <div className="flex flex-wrap items-center gap-2">
                    <button
                      type="button"
                      onClick={() => {
                        setConfirmarRetiro(true);
                        setErrorRetiro(null);
                      }}
                      className="inline-flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-xs font-semibold border-[#DFE5ED] transition hover:border-[#C2410C] dark:border-white/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
                      style={{ color: '#C2410C' }}
                    >
                      <Archive className="h-3.5 w-3.5" aria-hidden="true" />
                      Retirar del catálogo
                    </button>
                    {errorRetiro && (
                      <span className="text-xs" role="alert" style={{ color: '#C2410C' }}>
                        {errorRetiro}
                      </span>
                    )}
                  </div>
                )}
                {tipo.publicationStatus === 'archived' && (
                  <p className="text-xs opacity-70">
                    Este tipo está retirado del catálogo: no se puede elegir ni corregir.
                  </p>
                )}
              </header>

              <nav className="flex flex-wrap gap-1" aria-label="Configuración del tipo">
                {PESTANAS.map((p) => (
                  <button
                    key={p.id}
                    type="button"
                    onClick={() => setPestana(p.id)}
                    aria-current={pestana === p.id ? 'page' : undefined}
                    className="rounded-lg px-3 py-1.5 text-xs font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
                    style={
                      pestana === p.id
                        ? { background: 'rgba(85,126,255,0.12)', color: '#557EFF' }
                        : { color: '#162744' }
                    }
                  >
                    {p.label}
                  </button>
                ))}
              </nav>

              {cargandoDetalle ? (
                <p className="flex items-center gap-2 py-6 text-xs opacity-70">
                  <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                  Cargando la configuración…
                </p>
              ) : errorDetalle ? (
                <p className="py-6 text-xs" role="alert" style={{ color: '#C2410C' }}>
                  {errorDetalle}
                </p>
              ) : (
                <>
                  {pestana === 'identidad' && (
                    <TipoTramiteIdentidad tipo={tipo} onGuardado={aplicar} />
                  )}
                  {pestana === 'capacidades' && detalle && (
                    <TipoTramiteCapacidades perfil={detalle.perfil} onGuardado={recargarDetalle} />
                  )}
                  {pestana === 'recorrido' && detalle && (
                    <TipoTramiteRecorrido
                      procedureTypeId={tipo.id}
                      pasos={detalle.pasos}
                      onGuardado={recargarDetalle}
                    />
                  )}
                  {pestana === 'documentos' && detalle && (
                    <TipoTramiteDocumentos perfil={detalle.perfil} onGuardado={recargarDetalle} />
                  )}
                  {pestana === 'quipux' && detalle && (
                    <TipoTramiteQuipux
                      procedureTypeId={tipo.id}
                      familiaFlit={tipo.family}
                      gateProfile={detalle.perfil.gateProfile}
                    />
                  )}
                </>
              )}
            </div>
          )}
        </div>
      </div>

      {creando && (
        <NuevoTipoTramiteModal
          onCerrar={() => setCreando(false)}
          onCreado={(nuevo) => {
            setCreando(false);
            setTipos((prev) => [...prev, nuevo]);
            setSeleccionado(nuevo.id);
            setPestana('capacidades');
          }}
        />
      )}

      {confirmarRetiro && tipo && (
        <ConfirmarRetiro
          nombre={tipo.name}
          retirando={retirando}
          onCancelar={() => setConfirmarRetiro(false)}
          onConfirmar={() => void retirar()}
        />
      )}
    </div>
  );
}

/**
 * Confirmación del retiro. Dice qué hace de verdad —archivar, no borrar— porque «eliminar» sugiere
 * una pérdida de datos que no ocurre, y saberlo cambia si el gestor se atreve a pulsar.
 */
function ConfirmarRetiro({
  nombre,
  retirando,
  onCancelar,
  onConfirmar,
}: {
  nombre: string;
  retirando: boolean;
  onCancelar: () => void;
  onConfirmar: () => void;
}) {
  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ background: 'rgba(22,39,68,0.45)' }}
      role="dialog"
      aria-modal="true"
      aria-label="Retirar tipo de trámite"
    >
      <div className="w-full max-w-md rounded-2xl border bg-white p-5 border-[#DFE5ED] dark:border-white/10 dark:bg-[#0B0F14]">
        <h2 className="text-base font-semibold text-[#162744] dark:text-white">
          Retirar «{nombre}» del catálogo
        </h2>
        <p className="mt-2 text-xs leading-relaxed opacity-80">
          El tipo se archiva y deja de poder elegirse: no se borra, y su historial queda intacto. Si
          tiene trámites, la operación se rechaza — quedarían apuntando a un tipo retirado.
        </p>
        <div className="mt-5 flex justify-end gap-2">
          <button
            type="button"
            onClick={onCancelar}
            className="rounded-xl border px-4 py-2 text-xs font-medium border-[#DFE5ED] dark:border-white/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={onConfirmar}
            disabled={retirando}
            className="rounded-xl px-5 py-2 text-xs font-semibold text-white disabled:opacity-40 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
            style={{ background: '#C2410C' }}
          >
            {retirando ? 'Retirando…' : 'Sí, retirar'}
          </button>
        </div>
      </div>
    </div>
  );
}

/**
 * Resultado de la validación del tipo. Se pinta siempre que haya errores: son exactamente lo que
 * impide habilitarlo, y descubrirlos al pulsar el interruptor obliga a un intento fallido para
 * enterarse de algo que ya se sabía.
 */
function Validacion({ detalle }: { detalle: { validacion: { isValid: boolean; errors: { message: string }[] } | null } | null }) {
  const v = detalle?.validacion;
  if (!v || v.isValid) return null;

  return (
    <div
      className="rounded-xl border px-3 py-2.5 text-xs"
      style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.08)' }}
      role="status"
    >
      <p className="mb-1.5 flex items-center gap-1.5 font-semibold" style={{ color: '#B87A00' }}>
        <AlertTriangle className="h-3.5 w-3.5" aria-hidden="true" />
        La parametrización tiene {v.errors.length} {v.errors.length === 1 ? 'problema' : 'problemas'}
      </p>
      <ul className="ml-4 list-disc space-y-1 text-[#162744]/80 dark:text-white/80">
        {v.errors.map((e) => (
          <li key={e.message}>{e.message}</li>
        ))}
      </ul>
    </div>
  );
}
