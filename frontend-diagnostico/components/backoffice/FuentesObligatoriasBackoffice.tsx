"use client";

import { useEffect, useId, useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS, ETIQUETA_CLS, MARCA_CLS, OPCION_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { TOOLTIP_CLS } from "@/components/parametrizacion/estilos";
import { nombreFuente } from "@/lib/configuracion";
import {
  FUENTES_CONFIGURABLES,
  guardarFuentesObligatorias,
  listarFuentesObligatorias,
  tituloDefinicion,
  type DefinicionFuentesObligatorias,
  type FuenteConfigurable,
} from "@/lib/backoffice/fuentes-obligatorias";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";

/**
 * Configurador de fuentes obligatorias (ADR-0078) en /backoffice/fuentes-obligatorias.
 * Muestra solo las cabeceras que devuelve el GET: una con `fuentes: []` es lista vacía guardada.
 * No resuelve herencia y no llama GET /tipos-tramite. RUNT no tiene casilla.
 * El piso SIMIT_PROPIETARIO y los tres SIMIT del unilateral van bloqueados, enfocables, con el motivo en texto.
 */

const ALCANCES_PISO = new Set(["TRASPASO", "TRASPASO_STANDARD", "TRASPASO_TRANSFERENCIA_DE_DOMINIO"]);
const SIMIT_UNILATERAL = new Set<FuenteConfigurable>(["SIMIT_VEHICULO", "SIMIT_PROPIETARIO", "SIMIT_LOCATARIO"]);
const UNILATERAL = "TRASPASO_UNILATERAL";

const MOTIVO_PISO = "El paz y salvo del propietario es obligatorio en este alcance y no se puede quitar.";
const MOTIVO_UNILATERAL = "En el traspaso unilateral no se exige el SIMIT y no se puede marcar.";

interface Borrador {
  clave: string;
  familia: string | null;
  tipo_tramite_codigo: string | null;
  /** Título con el nombre de la familia o del tipo (Punto 5); se calcula una vez al leer, no viaja en el PUT. */
  titulo: string;
  fuentes: FuenteConfigurable[];
  guardando: boolean;
  error: string | null;
}

type Carga = { tipo: "cargando" } | { tipo: "error"; error: unknown } | { tipo: "listo"; definiciones: Borrador[] };

function codigoAlcance(def: { familia: string | null; tipo_tramite_codigo: string | null }): string {
  return def.familia ?? def.tipo_tramite_codigo ?? "";
}

function motivoBloqueo(alcance: string, fuente: FuenteConfigurable): string | null {
  if (ALCANCES_PISO.has(alcance) && fuente === "SIMIT_PROPIETARIO") return MOTIVO_PISO;
  if (alcance === UNILATERAL && SIMIT_UNILATERAL.has(fuente)) return MOTIVO_UNILATERAL;
  return null;
}

function marcada(alcance: string, fuente: FuenteConfigurable, fuentes: readonly FuenteConfigurable[]): boolean {
  if (ALCANCES_PISO.has(alcance) && fuente === "SIMIT_PROPIETARIO") return true;
  return fuentes.includes(fuente);
}

function fuentesSeleccionadas(alcance: string, fuentes: readonly FuenteConfigurable[]): FuenteConfigurable[] {
  return FUENTES_CONFIGURABLES.filter((f) => marcada(alcance, f, fuentes));
}

function aBorrador(def: DefinicionFuentesObligatorias): Borrador {
  const alcance = codigoAlcance(def);
  const crudas = (Array.isArray(def.fuentes) ? def.fuentes : []) as readonly string[];
  const presentes = new Set(FUENTES_CONFIGURABLES.filter((f) => crudas.includes(f)));
  if (ALCANCES_PISO.has(alcance)) presentes.add("SIMIT_PROPIETARIO");
  return {
    clave: def.familia ?? def.tipo_tramite_codigo ?? "sin-alcance",
    familia: def.familia,
    tipo_tramite_codigo: def.tipo_tramite_codigo,
    titulo: tituloDefinicion(def),
    fuentes: FUENTES_CONFIGURABLES.filter((f) => presentes.has(f)),
    guardando: false,
    error: null,
  };
}

function Vacio() {
  return (
    <div className="flex flex-col items-center gap-3 py-10 text-center">
      <Icono nombre="portapapeles" className="h-10 w-10 text-flit-brand" />
      <h2 className="text-lg font-semibold">No hay definiciones guardadas</h2>
      <p className="max-w-md text-sm text-muted-foreground">Esta pantalla solo muestra las cabeceras que ya existen.</p>
    </div>
  );
}

export function FuentesObligatoriasBackoffice() {
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);

  useEffect(() => {
    const control = new AbortController();
    listarFuentesObligatorias(control.signal).then(
      (lista) => {
        if (control.signal.aborted) return;
        if (!lista || !Array.isArray(lista.definiciones)) {
          setCarga({ tipo: "error", error: new Error("La respuesta no trae definiciones.") });
          return;
        }
        setCarga({ tipo: "listo", definiciones: lista.definiciones.map(aBorrador) });
      },
      (error: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error });
      },
    );
    return () => control.abort();
  }, [intento]);

  function actualizar(clave: string, cambios: Partial<Borrador>) {
    setCarga((actual) => {
      if (actual.tipo !== "listo") return actual;
      return { tipo: "listo", definiciones: actual.definiciones.map((d) => (d.clave === clave ? { ...d, ...cambios } : d)) };
    });
  }

  function alternar(def: Borrador, fuente: FuenteConfigurable) {
    if (motivoBloqueo(codigoAlcance(def), fuente) || def.guardando) return;
    const fuentes = def.fuentes.includes(fuente) ? def.fuentes.filter((f) => f !== fuente) : fuentesSeleccionadas(codigoAlcance(def), [...def.fuentes, fuente]);
    actualizar(def.clave, { fuentes, error: null });
  }

  async function guardar(def: Borrador) {
    const alcance = codigoAlcance(def);
    actualizar(def.clave, { guardando: true, error: null });
    try {
      const guardada = await guardarFuentesObligatorias({
        familia: def.familia,
        tipo_tramite_codigo: def.tipo_tramite_codigo,
        fuentes: fuentesSeleccionadas(alcance, def.fuentes),
      });
      actualizar(def.clave, {
        guardando: false,
        error: null,
        familia: guardada.familia,
        tipo_tramite_codigo: guardada.tipo_tramite_codigo,
        fuentes: aBorrador(guardada).fuentes,
      });
    } catch (error: unknown) {
      actualizar(def.clave, {
        guardando: false,
        error: error instanceof Error ? error.message : "No se pudo guardar esta definición.",
      });
    }
  }

  let cuerpo: React.ReactNode;
  if (carga.tipo === "cargando") {
    cuerpo = (
      <section aria-label="Definiciones" className={TARJETA_CLS}>
        <VehicleLoader label="Cargando las fuentes obligatorias…" />
      </section>
    );
  } else if (carga.tipo === "error") {
    cuerpo = (
      <section aria-label="Definiciones" className={TARJETA_CLS}>
        <InlineAlert
          tone="error"
          title="No se pudieron cargar las fuentes obligatorias"
          action={
            <button
              type="button"
              onClick={() => {
                setCarga({ tipo: "cargando" });
                setIntento((n) => n + 1);
              }}
              className={BOTON_REINTENTAR_CLS}
            >
              Reintentar
            </button>
          }
        >
          {carga.error instanceof Error ? carga.error.message : "Intenta de nuevo en un momento."}
        </InlineAlert>
      </section>
    );
  } else if (carga.definiciones.length === 0) {
    cuerpo = (
      <section aria-label="Definiciones" className={TARJETA_CLS}>
        <Vacio />
      </section>
    );
  } else {
    cuerpo = (
      <div className="flex flex-col gap-4">
        {carga.definiciones.map((def) => (
          <DefinicionCard key={def.clave} def={def} onAlternar={(fuente) => alternar(def, fuente)} onGuardar={() => guardar(def)} />
        ))}
      </div>
    );
  }

  return (
    <>
      <CabeceraModulo
        as="section"
        aria-labelledby="fuentes-obl-titulo"
        tituloId="fuentes-obl-titulo"
        titulo="Fuentes obligatorias"
        descripcion={
          <>
            <p>
              Define las fuentes que el alta exige, por familia o por tipo de trámite. La lista es de FLIT y es la misma para todas las compañías.
            </p>
            <p className="mt-2">El RUNT del vehículo sigue siendo obligatorio en todo diagnóstico y no se configura aquí.</p>
          </>
        }
        icono="fuentes-oficiales"
      />
      {cuerpo}
    </>
  );
}

function DefinicionCard({ def, onAlternar, onGuardar }: { def: Borrador; onAlternar: (fuente: FuenteConfigurable) => void; onGuardar: () => void }) {
  const baseId = useId();
  const tituloId = `${baseId}-titulo`;
  const alcance = codigoAlcance(def);
  const titulo = def.titulo;
  const seleccion = fuentesSeleccionadas(alcance, def.fuentes);

  return (
    <section aria-labelledby={tituloId} className={`${TARJETA_CLS} flex flex-col gap-4`}>
      <h2 id={tituloId} className="text-lg font-semibold text-flit-primary">
        {titulo}
      </h2>
      {seleccion.length === 0 && <p className="text-sm text-muted-foreground">Definición guardada con la lista vacía.</p>}
      <fieldset className="flex flex-col gap-2">
        <legend className={ETIQUETA_CLS}>Fuentes</legend>
        <ul className="flex flex-col gap-2">
          {FUENTES_CONFIGURABLES.map((fuente) => (
            <CasillaFuente
              key={fuente}
              id={`${baseId}-${fuente}`}
              fuente={fuente}
              marcada={marcada(alcance, fuente, def.fuentes)}
              motivo={motivoBloqueo(alcance, fuente)}
              disabled={def.guardando}
              onAlternar={() => onAlternar(fuente)}
            />
          ))}
        </ul>
      </fieldset>
      {def.error && (
        <InlineAlert tone="error" title="No se guardó esta definición">
          {def.error}
        </InlineAlert>
      )}
      <div>
        <button type="button" onClick={onGuardar} disabled={def.guardando} className={BOTON_PRIMARIO_CLS}>
          {def.guardando ? "Guardando…" : "Guardar"}
        </button>
      </div>
    </section>
  );
}

function CasillaFuente({
  id,
  fuente,
  marcada,
  motivo,
  disabled,
  onAlternar,
}: {
  id: string;
  fuente: FuenteConfigurable;
  marcada: boolean;
  motivo: string | null;
  disabled: boolean;
  onAlternar: () => void;
}) {
  const tooltipId = `${id}-tooltip`;

  return (
    <li className="group relative">
      <label className={OPCION_CLS}>
        <input
          type="checkbox"
          value={fuente}
          checked={marcada}
          onChange={() => {
            if (!motivo) onAlternar();
          }}
          aria-disabled={motivo ? true : undefined}
          aria-describedby={motivo ? tooltipId : undefined}
          disabled={disabled}
          className={MARCA_CLS}
        />
        <span className="flex-1">{nombreFuente(fuente)}</span>
        {motivo && <Icono nombre="candado" className="h-3.5 w-3.5 text-muted-foreground" />}
      </label>
      {motivo && (
        <span role="tooltip" id={tooltipId} className={TOOLTIP_CLS}>
          {motivo}
        </span>
      )}
    </li>
  );
}
