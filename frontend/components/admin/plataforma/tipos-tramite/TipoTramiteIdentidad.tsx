'use client';

import { useState } from 'react';
import { Loader2 } from 'lucide-react';
import { superadminClient } from '@/lib/api/superadmin-client';
import { FAMILIA_OPCIONES } from '@/lib/api/types/familia-labels';
import type {
  ProcedureFamily,
  ProcedureTypeSummary,
} from '@/lib/api/types/procedure-parametrization';

const CAMPO =
  'w-full rounded-xl border px-3 py-2 text-xs border-[#DFE5ED] bg-white text-[#162744] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]';
const ETIQUETA = 'text-xs font-semibold text-[#162744] dark:text-white';

/**
 * Identidad del tipo: nombre, descripción, familia y si está activo.
 *
 * Nada de esto es cosmético. El NOMBRE es el rótulo legal del mandato y de la portada del
 * expediente —un tipo mal nombrado se firma mal—, y la FAMILIA gobierna clasificación, filtros,
 * causales de rechazo y el bloqueo por compañía. El código no se edita: identifica al tipo en el
 * catálogo, en las integraciones y en los snapshots ya congelados.
 */
export function TipoTramiteIdentidad({
  tipo,
  onGuardado,
}: {
  tipo: ProcedureTypeSummary;
  onGuardado: (actualizado: ProcedureTypeSummary) => void;
}) {
  const [nombre, setNombre] = useState(tipo.name);
  const [familia, setFamilia] = useState<ProcedureFamily>(tipo.family);
  const [activo, setActivo] = useState(tipo.isActive);
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ok, setOk] = useState(false);

  const sinCambios = nombre.trim() === tipo.name && familia === tipo.family && activo === tipo.isActive;

  const guardar = async () => {
    setGuardando(true);
    setError(null);
    setOk(false);
    try {
      const actualizado = await superadminClient.updateProcedureType(tipo.id, {
        name: nombre.trim(),
        isActive: activo,
        family: familia,
      });
      onGuardado(actualizado);
      setOk(true);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'No se pudo guardar.');
    } finally {
      setGuardando(false);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="flex flex-col gap-1">
          <span className={ETIQUETA}>Nombre</span>
          <input
            className={CAMPO}
            value={nombre}
            onChange={(e) => setNombre(e.target.value)}
            // El nombre accesible se declara aparte: la ayuda vive dentro del <label>, así que sin
            // esto el lector de pantalla anunciaría etiqueta y explicación como una sola frase.
            aria-label="Nombre"
            aria-describedby="nombre-nota"
          />
          <span id="nombre-nota" className="text-xs opacity-60">
            Es el rótulo del trámite en el mandato y en la portada del expediente.
          </span>
        </label>

        <label className="flex flex-col gap-1">
          <span className={ETIQUETA}>Familia</span>
          <select
            className={CAMPO}
            value={familia}
            aria-label="Familia"
            onChange={(e) => setFamilia(e.target.value as ProcedureFamily)}
          >
            {FAMILIA_OPCIONES.map((f) => (
              <option key={f.value} value={f.value}>
                {f.label}
              </option>
            ))}
          </select>
          <span className="text-xs opacity-60">
            Gobierna filtros, causales de rechazo y el bloqueo por compañía.
          </span>
        </label>
      </div>

      <label className="flex items-center gap-2">
        <input
          type="checkbox"
          checked={activo}
          onChange={(e) => setActivo(e.target.checked)}
          className="h-3.5 w-3.5 accent-[#557EFF]"
        />
        <span className="text-xs text-[#162744] dark:text-white">
          Activo en el catálogo
        </span>
      </label>

      <div className="flex items-center gap-3">
        <button
          type="button"
          onClick={() => void guardar()}
          disabled={guardando || sinCambios || nombre.trim().length === 0}
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-40 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
        >
          {guardando ? (
            <span className="inline-flex items-center gap-1.5">
              <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />
              Guardando…
            </span>
          ) : (
            'Guardar cambios'
          )}
        </button>
        {tipo.publicationStatus === 'published' && !sinCambios && (
          <span className="text-xs opacity-70">
            El tipo está publicado: guardar sube su versión. Los trámites en curso no cambian.
          </span>
        )}
        {ok && (
          <span className="text-xs font-medium" style={{ color: '#0E9F6E' }} role="status">
            Guardado
          </span>
        )}
      </div>

      {error && (
        <p className="text-xs" role="alert" style={{ color: '#C2410C' }}>
          {error}
        </p>
      )}
    </div>
  );
}
