'use client';

import { useState } from 'react';
import type { ProcedureFamily, ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';
import { FAMILY_DESCRIPTION, FAMILY_LABEL } from '@/lib/api/types/procedure-parametrization';
import { useTiposHabilitados } from '@/hooks/useTiposHabilitados';

/** Bloqueo de creación por familia que la compañía tiene configurado. */
export interface FamiliasBloqueadas {
  matriculas?: boolean;
  traspaso?: boolean;
  otros?: boolean;
}

interface Props {
  /** Se invoca con el `code` del tipo elegido. */
  onElegir: (code: string) => void;
  bloqueadas?: FamiliasBloqueadas;
}

const BLOQUEO_POR_FAMILIA: Record<ProcedureFamily, keyof FamiliasBloqueadas> = {
  MATRICULAS: 'matriculas',
  TRASPASO: 'traspaso',
  OTROS: 'otros',
};

/**
 * Selección **familia → tipo** para crear un trámite (ADR-0050).
 *
 * Sustituye a las tres tarjetas fijas del paso 1, que estaban escritas a mano y dejaban "Otros
 * trámites" apagada porque no existía recorrido. Ahora las familias y los tipos salen del catálogo:
 * habilitar un tipo es configuración, no un despliegue.
 *
 * Solo se listan los tipos con la barrera `wizardEnabled` encendida, así que el selector nunca
 * ofrece un trámite sin recorrido, documentos o causales.
 */
export function SelectorTipoTramite({ onElegir, bloqueadas }: Props) {
  const { familias, status, error, reload } = useTiposHabilitados();
  const [familiaAbierta, setFamiliaAbierta] = useState<ProcedureFamily | null>(null);

  if (status === 'loading') {
    return <p className="text-sm opacity-70">Cargando tipos de trámite…</p>;
  }

  if (status === 'error') {
    return (
      <div className="rounded-xl border p-4" style={{ borderColor: '#FCA5A5', background: '#FEF2F2' }}>
        <p className="text-sm" style={{ color: '#991B1B' }}>{error}</p>
        <button type="button" onClick={() => void reload()} className="mt-2 text-xs font-semibold underline">
          Reintentar
        </button>
      </div>
    );
  }

  if (familias.length === 0) {
    // No es un error: es el estado legítimo mientras ningún tipo tiene la barrera encendida.
    return (
      <p className="text-sm opacity-70">
        No hay tipos de trámite habilitados para crear. Comunícate con el administrador.
      </p>
    );
  }

  const abierta = familias.find((f) => f.family === familiaAbierta);

  // ── Paso 2: los tipos de la familia elegida ────────────────────────────────
  if (abierta) {
    return (
      <fieldset className="space-y-2">
        <legend className="sr-only">Tipo de trámite de {FAMILY_LABEL[abierta.family]}</legend>

        <button
          type="button"
          onClick={() => setFamiliaAbierta(null)}
          className="text-xs font-semibold underline focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          style={{ color: '#557EFF' }}
        >
          ← {FAMILY_LABEL[abierta.family]}
        </button>

        <div className="space-y-2">
          {abierta.tipos.map((tipo: ProcedureTypeSummary) => (
            <button
              key={tipo.code}
              type="button"
              onClick={() => onElegir(tipo.code)}
              className="w-full rounded-xl border p-3.5 text-left transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 hover:border-[#557EFF]"
            >
              <p className="text-xs font-semibold" style={{ color: '#162744' }}>{tipo.name}</p>
            </button>
          ))}
        </div>
      </fieldset>
    );
  }

  // ── Paso 1: familias con tipos habilitados ────────────────────────────────
  return (
    <fieldset className="space-y-2">
      <legend className="sr-only">Familia del trámite</legend>

      {familias.map(({ family, tipos }) => {
        const bloqueada = bloqueadas?.[BLOQUEO_POR_FAMILIA[family]] === true;
        return (
          <button
            key={family}
            type="button"
            onClick={() => setFamiliaAbierta(family)}
            disabled={bloqueada}
            className="w-full rounded-xl border p-3.5 text-left transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed enabled:hover:border-[#557EFF]"
            style={bloqueada ? { opacity: 0.55 } : undefined}
          >
            <p className="text-xs font-semibold" style={{ color: '#162744' }}>{FAMILY_LABEL[family]}</p>
            <p className="mt-0.5 text-xs opacity-70">
              {bloqueada
                ? `${FAMILY_DESCRIPTION[family]} · no habilitado para tu compañía`
                : `${FAMILY_DESCRIPTION[family]} · ${tipos.length} ${tipos.length === 1 ? 'trámite' : 'trámites'}`}
            </p>
          </button>
        );
      })}
    </fieldset>
  );
}
