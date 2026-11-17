"use client";

import { useId } from "react";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Tooltip } from "@flit/ui/Tooltip";
import { cn } from "@flit/ui/cn";
import { AYUDA_FUENTE_NO_DISPONIBLE, nombreFuente, type Fuente } from "@/lib/configuracion";
import { FUENTE_SIEMPRE, GRUPOS, fuentesOfrecidas, type GrupoAlcance } from "@/lib/diagnosticos/api";
import { AYUDA_CLS, ETIQUETA_CLS, MARCA_CLS, OPCION_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { CONTADOR_CLS, ERROR_CLS, INSIGNIA_CLS, REJILLA_FUENTES_CLS } from "./estilos";

export interface SeccionAlcanceProps {
  habilitadas: readonly Fuente[];
  /** `fuentes_disponibles` de la configuración (HU #13865): única fuente de la disponibilidad. */
  disponibles: readonly Fuente[];
  seleccionadas: readonly Fuente[];
  obligatorias: readonly Fuente[];
  onAlternar: (fuente: Fuente) => void;
  error?: string;
  disabled?: boolean;
  compacta?: boolean;
}

/**
 * Por qué dependen las fuentes de persona: no son fuentes sueltas, salen de lo que reporta el RUNT vehículo. Desde la
 * HU #14585 el documento declarado (propietario actual) ya no alimenta las de locatario (contrato §1, C3).
 */
const DEPENDENCIA: Partial<Record<GrupoAlcance, string>> = {
  por_propietario: "Depende del propietario que reporta el RUNT vehículo.",
  por_locatario: "Depende del locatario que reporta el RUNT vehículo.",
};

const MOTIVO_RUNT = "El RUNT vehículo siempre se consulta: es la base del diagnóstico.";
const MOTIVO_OBLIGATORIA = "Fuente obligatoria para el tipo de trámite elegido.";

/**
 * HU #14583 (spec-ui §2): el contador «N de M fuentes» cuenta sobre las ofrecidas a la compañía y va en la línea del
 * título, anunciado (aria-live). Los grupos van en filas y sus fuentes en horizontal, todas en la misma rejilla fija por
 * container query (mismo ancho de tarjeta en todos los grupos). Un grupo sin ninguna fuente contratada no se pinta; uno
 * con contratadas sin consulta automática sigue visible con sus casillas deshabilitadas.
 *
 * Sección Alcance (HU #13752, F01-C2/C3/C4/C7): las fuentes en sus tres grupos (R-24), solo las habilitadas del contrato
 * que el backend declara en `fuentes_disponibles` (HU #13865: el impuesto departamental sale solo cuando llega ahí). RUNT vehículo y las obligatorias del tipo van marcadas y
 * bloqueadas con tooltip; las casillas bloqueadas siguen enfocables (aria-disabled) para que el tooltip llegue por teclado.
 */
export function SeccionAlcance({ habilitadas, disponibles, seleccionadas, obligatorias, onAlternar, error, disabled = false, compacta = false }: SeccionAlcanceProps) {
  const id = useId();
  const errorId = `${id}-error`;
  const tituloId = `${id}-titulo`;
  const ofrecidas = fuentesOfrecidas(habilitadas, disponibles);
  const faltantes = obligatorias.filter((f) => !ofrecidas.includes(f));
  // Contratadas pero sin consulta automatizada en producción (fuera de fuentes_disponibles): se ven, no se eligen (AC16).
  const noDisponibles = habilitadas.filter((f) => !ofrecidas.includes(f));
  const marcadas = ofrecidas.filter((f) => f === FUENTE_SIEMPRE || obligatorias.includes(f) || seleccionadas.includes(f)).length;
  const Titulo = compacta ? "h3" : "h2";

  return (
    <section className={`@container ${compacta ? "flex flex-col gap-3" : `${TARJETA_CLS} flex flex-col gap-4`}`}>
      {/* El nombre del grupo es solo el título: el contador va en la misma línea pero no entra en el nombre. */}
      <fieldset aria-labelledby={tituloId} aria-describedby={error ? errorId : undefined} className="flex flex-col gap-4">
        <legend className="mb-1 flex flex-wrap items-center gap-3">
          <Titulo id={tituloId} className={compacta ? ETIQUETA_CLS : "text-lg font-semibold text-flit-primary"}>
            Alcance
          </Titulo>
          <span aria-live="polite" aria-atomic="true" className={CONTADOR_CLS}>
            <span className="font-bold tabular-nums">{marcadas}</span> de <span className="font-bold tabular-nums">{ofrecidas.length}</span>{" "}
            fuentes<span className="sr-only"> seleccionadas</span>
          </span>
        </legend>
        {!compacta && <p className={AYUDA_CLS}>Elige qué fuentes consultar por cada vehículo. Solo ves las que tu compañía tiene contratadas.</p>}
        {faltantes.length > 0 && (
          <InlineAlert tone="warning" title="Fuentes obligatorias no contratadas">
            El tipo de trámite exige {faltantes.map(nombreFuente).join(", ")}, que tu contrato no incluye. Pide al backoffice FLIT que la habilite o
            elige otro tipo.
          </InlineAlert>
        )}
        <div className="flex flex-col gap-4">
          {GRUPOS.flatMap((g) => {
            const fuentes = g.fuentes.filter((f) => ofrecidas.includes(f));
            const sinConsulta = g.fuentes.filter((f) => noDisponibles.includes(f));
            // Grupo sin ninguna fuente contratada: no se pinta (A2.4).
            return fuentes.length === 0 && sinConsulta.length === 0 ? [] : [{ g, fuentes, sinConsulta }];
          }).map(({ g, fuentes, sinConsulta }, i) => {
            const dependencia = DEPENDENCIA[g.grupo];
            const descId = `${id}-${g.grupo}-desc`;
            const nombreId = `${id}-${g.grupo}-nombre`;
            return (
              <fieldset
                key={g.grupo}
                aria-labelledby={nombreId}
                aria-describedby={dependencia ? descId : undefined}
                className={`flex flex-col gap-2 ${i > 0 ? "border-t border-[color:var(--di-divider)] pt-4" : ""}`}
              >
                {/* Encabezado del grupo en una línea: el nombre y, a continuación, de qué depende. */}
                <legend className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
                  <span id={nombreId} className={ETIQUETA_CLS}>
                    {g.nombre}
                  </span>
                  {dependencia && (
                    <span id={descId} className={AYUDA_CLS}>
                      {dependencia}
                    </span>
                  )}
                </legend>
                <ul className={REJILLA_FUENTES_CLS}>
                    {fuentes.map((fuente) => {
                      const motivo = fuente === FUENTE_SIEMPRE ? MOTIVO_RUNT : obligatorias.includes(fuente) ? MOTIVO_OBLIGATORIA : null;
                      return (
                        <CasillaFuente
                          key={fuente}
                          id={`${id}-${fuente}`}
                          fuente={fuente}
                          marcada={motivo !== null || seleccionadas.includes(fuente)}
                          motivoBloqueo={motivo}
                          dependiente={dependencia !== undefined}
                          disabled={disabled}
                          onAlternar={onAlternar}
                        />
                      );
                    })}
                    {sinConsulta.map((fuente) => (
                      <CasillaNoDisponible key={fuente} id={`${id}-${fuente}`} fuente={fuente} />
                    ))}
                </ul>
              </fieldset>
            );
          })}
        </div>
      </fieldset>
      {error && (
        <p id={errorId} className={ERROR_CLS}>
          {error}
        </p>
      )}
    </section>
  );
}

function CasillaFuente({
  id,
  fuente,
  marcada,
  motivoBloqueo,
  dependiente,
  disabled,
  onAlternar,
}: {
  id: string;
  fuente: Fuente;
  marcada: boolean;
  motivoBloqueo: string | null;
  dependiente: boolean;
  disabled: boolean;
  onAlternar: (fuente: Fuente) => void;
}) {
  const tooltipId = `${id}-tooltip`;
  const insigniaId = `${id}-dependiente`;
  const descripcion = [dependiente ? insigniaId : null, motivoBloqueo ? tooltipId : null].filter(Boolean).join(" ") || undefined;

  const etiqueta = (
    <label className={cn(OPCION_CLS, "h-full min-h-11 items-start")}>
        <input
          type="checkbox"
          value={fuente}
          checked={marcada}
          onChange={() => {
            if (!motivoBloqueo) onAlternar(fuente);
          }}
          aria-disabled={motivoBloqueo ? true : undefined}
          aria-describedby={descripcion}
          disabled={disabled}
          className={MARCA_CLS}
        />
        {/* Nombre (puede partir en 2 líneas) y, debajo, la insignia «Dependiente»; el candado a la derecha. */}
        <span className="flex min-w-0 grow flex-col items-start gap-1">
          <span>{nombreFuente(fuente)}</span>
          {dependiente && (
            // Fuera del nombre accesible (aria-hidden) pero dentro de la descripción de la casilla (aria-describedby).
            <span id={insigniaId} aria-hidden="true" className={INSIGNIA_CLS}>
              Dependiente
            </span>
          )}
        </span>
        {motivoBloqueo && <Icono nombre="candado" className="ml-auto mt-0.5 h-3.5 w-3.5 shrink-0 text-muted-foreground" />}
    </label>
  );

  return (
    <li className="min-w-0">
      {motivoBloqueo ? (
        // Tooltip del kit (spec-ui §2.3): se abre al enfocar la casilla (el foco sube a la etiqueta) o al pasar el
        // puntero, y Esc lo cierra. Persistente: es un literal fijo y la casilla lo lee en su descripción.
        <Tooltip contenido={motivoBloqueo} id={tooltipId} persistente describirHijo={false}>
          {etiqueta}
        </Tooltip>
      ) : (
        etiqueta
      )}
    </li>
  );
}

/** Fuente contratada sin consulta automatizada todavía: deshabilitada, sin marcar y con la ayuda del contrato (AC16). */
function CasillaNoDisponible({ id, fuente }: { id: string; fuente: Fuente }) {
  const ayudaId = `${id}-no-disponible`;
  return (
    <li className="min-w-0">
      <label className={cn(OPCION_CLS, "min-h-11 cursor-not-allowed items-start opacity-70")}>
        <input type="checkbox" value={fuente} checked={false} disabled readOnly aria-describedby={ayudaId} className={MARCA_CLS} />
        <span className="min-w-0 grow">{nombreFuente(fuente)}</span>
      </label>
      <p id={ayudaId} className={`${AYUDA_CLS} mt-1`}>
        {AYUDA_FUENTE_NO_DISPONIBLE}
      </p>
    </li>
  );
}
