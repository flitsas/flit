'use client';

import { useState } from 'react';
import type { ProcedureFamily, ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';
import { FAMILY_DESCRIPTION, FAMILY_LABEL } from '@/lib/api/types/procedure-parametrization';
import { useTiposHabilitados } from '@/hooks/useTiposHabilitados';
import { WizardCardHeader } from './wizard-atoms';

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
  /** Salir sin elegir. Sin él no se pinta el botón. */
  onCancelar?: () => void;
  /**
   * El contenedor ya pinta el título y el subtítulo de la elección — el caso del modal, cuya
   * cabecera los renderiza y les da el `aria-labelledby` del diálogo.
   *
   * Solo calla la cabecera del PASO 1, que es la que diría lo mismo dos veces. La del paso 2 se
   * conserva: nombra la familia en la que entraste, que es información nueva y no un duplicado del
   * título del diálogo.
   */
  tituloEnContenedor?: boolean;
}

const BLOQUEO_POR_FAMILIA: Record<ProcedureFamily, keyof FamiliasBloqueadas> = {
  MATRICULAS: 'matriculas',
  TRASPASO: 'traspaso',
  OTROS: 'otros',
};

/**
 * Tarjeta de opción del selector.
 *
 * Es el MISMO patrón «select tipo tarjeta» que la propuesta usa para elegir el trámite principal:
 * rejilla de tarjetas blancas con borde, título y una frase que dice qué resuelve, para leerlas de
 * un vistazo en vez de abrir una lista. Se extrae a un componente porque los dos pasos —familia y
 * tipo— son la misma tarjeta con distinto contenido; tenerlas escritas dos veces fue lo que dejó el
 * paso de tipos como una lista de nombres sueltos mientras el de familias sí tenía descripción.
 *
 * Deshabilitada no se oculta: el gestor la busca, y desaparecerla le haría creer que el trámite no
 * existe en vez de que su compañía no lo tiene habilitado. Por eso el motivo viaja en el texto, no
 * solo en la opacidad — un estado nunca depende solo del color.
 */
function OpcionTarjeta({
  titulo,
  descripcion,
  onClick,
  disabled = false,
}: {
  titulo: string;
  descripcion?: string;
  onClick: () => void;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="flex h-full w-full flex-col rounded-xl border bg-white p-3.5 text-left transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed enabled:hover:border-[#557EFF] enabled:hover:shadow-sm dark:bg-[#162744]"
      style={{ borderColor: '#DFE5ED', ...(disabled ? { opacity: 0.55 } : {}) }}
    >
      <span className="text-xs font-semibold" style={{ color: '#162744' }}>
        {titulo}
      </span>
      {descripcion ? (
        // opacity-70 es el piso del sistema sobre texto: por debajo el contraste efectivo cae de AA.
        <span className="mt-0.5 text-xs opacity-70">{descripcion}</span>
      ) : null}
    </button>
  );
}

/** Botón de salida del selector: secundario, tarjeta blanca con borde (no un CTA degradado). */
function BotonCancelar({ onClick, children }: { onClick: () => void; children: string }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="mt-5 rounded-xl border bg-white px-4 py-2 text-xs font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 hover:border-[#557EFF] dark:bg-[#162744]"
      style={{ borderColor: '#DFE5ED', color: '#162744' }}
    >
      {children}
    </button>
  );
}

/**
 * Selección **familia → tipo** para crear un trámite (ADR-0050).
 *
 * Sustituye a las tres tarjetas fijas del paso 1, que estaban escritas a mano y dejaban "Otros
 * trámites" apagada porque no existía recorrido. Ahora las familias y los tipos salen del catálogo:
 * habilitar un tipo es configuración, no un despliegue.
 *
 * Conserva la composición de aquella pantalla —cabecera con título y subtítulo, rejilla de tarjetas
 * y salida— porque es el patrón de la propuesta; lo que cambia es de dónde salen las opciones. El
 * paso de tipos usa la misma tarjeta que el de familias: en la familia OTROS son quince trámites, y
 * una lista vertical de nombres sueltos no deja distinguir un «Traslado de cuenta» de un «Radicado
 * de cuenta».
 *
 * Solo se listan los tipos con la barrera `wizardEnabled` encendida, así que el selector nunca
 * ofrece un trámite sin recorrido, documentos o causales.
 */
export function SelectorTipoTramite({
  onElegir,
  bloqueadas,
  onCancelar,
  tituloEnContenedor = false,
}: Props) {
  const { familias, status, error, reload } = useTiposHabilitados();
  const [familiaAbierta, setFamiliaAbierta] = useState<ProcedureFamily | null>(null);

  if (status === 'loading') {
    return <p className="text-sm opacity-70">Cargando tipos de trámite…</p>;
  }

  if (status === 'error') {
    return (
      <div className="rounded-xl border p-4" style={{ borderColor: 'rgba(255,78,0,0.32)', background: 'rgba(255,78,0,0.12)' }}>
        <p className="text-sm" style={{ color: '#C2410C' }}>{error}</p>
        <button
          type="button"
          onClick={() => void reload()}
          className="mt-2 text-xs font-semibold underline focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          style={{ color: '#C2410C' }}
        >
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
      <fieldset className="min-w-0">
        <legend className="sr-only">Tipo de trámite de {FAMILY_LABEL[abierta.family]}</legend>

        <WizardCardHeader
          title={FAMILY_LABEL[abierta.family]}
          subtitle={`${FAMILY_DESCRIPTION[abierta.family]}. Elige el trámite que vas a radicar.`}
        />

        {/* Dos columnas y no tres: los nombres de los tipos son frases («Levantamiento de prenda»,
            «Conversión de combustible») y a tres columnas se parten en tres líneas. */}
        <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
          {abierta.tipos.map((tipo: ProcedureTypeSummary) => (
            <OpcionTarjeta key={tipo.code} titulo={tipo.name} onClick={() => onElegir(tipo.code)} />
          ))}
        </div>

        <BotonCancelar onClick={() => setFamiliaAbierta(null)}>
          ← Volver a las familias
        </BotonCancelar>
      </fieldset>
    );
  }

  // ── Paso 1: familias con tipos habilitados ────────────────────────────────
  return (
    <fieldset className="min-w-0">
      <legend className="sr-only">Familia del trámite</legend>

      {tituloEnContenedor ? null : (
        <WizardCardHeader
          title="Selecciona el tipo de trámite"
          subtitle="Define el trámite principal que se radicará con este expediente."
        />
      )}

      <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-3">
        {familias.map(({ family, tipos }) => {
          const bloqueada = bloqueadas?.[BLOQUEO_POR_FAMILIA[family]] === true;
          return (
            <OpcionTarjeta
              key={family}
              titulo={FAMILY_LABEL[family]}
              descripcion={
                bloqueada
                  ? `${FAMILY_DESCRIPTION[family]} · no habilitado para tu compañía`
                  : `${FAMILY_DESCRIPTION[family]} · ${tipos.length} ${tipos.length === 1 ? 'trámite' : 'trámites'}`
              }
              disabled={bloqueada}
              onClick={() => setFamiliaAbierta(family)}
            />
          );
        })}
      </div>

      {onCancelar ? <BotonCancelar onClick={onCancelar}>Cancelar</BotonCancelar> : null}
    </fieldset>
  );
}
