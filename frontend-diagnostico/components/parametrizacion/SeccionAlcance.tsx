"use client";

import { useId } from "react";
import { Lock } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { nombreFuente, type Fuente } from "@/lib/configuracion";
import { FUENTE_SIEMPRE, GRUPOS, fuentesOfrecidas, type GrupoAlcance } from "@/lib/diagnosticos/api";
import { AYUDA_CLS, ETIQUETA_CLS, MARCA_CLS, OPCION_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { ERROR_CLS, INSIGNIA_CLS, TOOLTIP_CLS } from "./estilos";

export interface SeccionAlcanceProps {
  habilitadas: readonly Fuente[];
  seleccionadas: readonly Fuente[];
  obligatorias: readonly Fuente[];
  onAlternar: (fuente: Fuente) => void;
  error?: string;
  disabled?: boolean;
  compacta?: boolean;
}

/** Por qué dependen las fuentes de persona: no son fuentes sueltas, salen del RUNT o del documento declarado. */
const DEPENDENCIA: Partial<Record<GrupoAlcance, string>> = {
  por_propietario: "Depende del propietario que reporta el RUNT vehículo.",
  por_locatario: "Depende del documento del locatario que declara el cliente en la carga.",
};

const MOTIVO_RUNT = "El RUNT vehículo siempre se consulta: es la base del diagnóstico.";
const MOTIVO_OBLIGATORIA = "Fuente obligatoria para el tipo de trámite elegido.";

/**
 * Sección Alcance (HU #13752, F01-C2/C3/C4/C7): las fuentes en sus tres grupos (R-24), solo las habilitadas del contrato
 * (sin IMPUESTO_DEPARTAMENTAL mientras el backend lo rechace). RUNT vehículo y las obligatorias del tipo van marcadas y
 * bloqueadas con tooltip; las casillas bloqueadas siguen enfocables (aria-disabled) para que el tooltip llegue por teclado.
 */
export function SeccionAlcance({ habilitadas, seleccionadas, obligatorias, onAlternar, error, disabled = false, compacta = false }: SeccionAlcanceProps) {
  const id = useId();
  const errorId = `${id}-error`;
  const ofrecidas = fuentesOfrecidas(habilitadas);
  const faltantes = obligatorias.filter((f) => !ofrecidas.includes(f));
  const Titulo = compacta ? "h3" : "h2";

  return (
    <section className={compacta ? "flex flex-col gap-3" : `${TARJETA_CLS} flex flex-col gap-4`}>
      <fieldset aria-describedby={error ? errorId : undefined} className="flex flex-col gap-4">
        <legend className="mb-1">
          <Titulo className={compacta ? ETIQUETA_CLS : "text-lg font-semibold text-flit-primary dark:text-white"}>Alcance</Titulo>
        </legend>
        {!compacta && <p className={AYUDA_CLS}>Elige qué fuentes consultar por cada vehículo. Solo ves las que tu compañía tiene contratadas.</p>}
        {faltantes.length > 0 && (
          <InlineAlert tone="warning" title="Fuentes obligatorias no contratadas">
            El tipo de trámite exige {faltantes.map(nombreFuente).join(", ")}, que tu contrato no incluye. Pide al backoffice FLIT que la habilite o
            elige otro tipo.
          </InlineAlert>
        )}
        <div className={compacta ? "grid gap-3" : "grid gap-4 lg:grid-cols-3"}>
          {GRUPOS.map((g) => {
            const fuentes = g.fuentes.filter((f) => ofrecidas.includes(f));
            const dependencia = DEPENDENCIA[g.grupo];
            const descId = `${id}-${g.grupo}-desc`;
            return (
              <fieldset key={g.grupo} aria-describedby={dependencia ? descId : undefined} className="flex flex-col gap-2">
                <legend className={ETIQUETA_CLS}>{g.nombre}</legend>
                {dependencia && (
                  <p id={descId} className={AYUDA_CLS}>
                    {dependencia}
                  </p>
                )}
                {fuentes.length === 0 ? (
                  <p className={AYUDA_CLS}>Tu contrato no incluye fuentes de este grupo.</p>
                ) : (
                  <ul className="flex flex-col gap-2">
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
                  </ul>
                )}
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

  return (
    <li className="group relative">
      <label className={OPCION_CLS}>
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
        <span className="flex-1">{nombreFuente(fuente)}</span>
        {dependiente && (
          // Fuera del nombre accesible (aria-hidden) pero dentro de la descripción de la casilla (aria-describedby).
          <span id={insigniaId} aria-hidden="true" className={INSIGNIA_CLS}>
            Dependiente
          </span>
        )}
        {motivoBloqueo && <Lock className="h-3.5 w-3.5 text-[var(--nav-texto)]" aria-hidden="true" />}
      </label>
      {motivoBloqueo && (
        <span role="tooltip" id={tooltipId} className={TOOLTIP_CLS}>
          {motivoBloqueo}
        </span>
      )}
    </li>
  );
}
