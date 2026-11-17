"use client";

import { useEffect, useId, useState, type ReactNode } from "react";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { TablaEditable, type ColumnaEditable } from "@flit/ui/TablaEditable";
import { BOTON_REINTENTAR_CLS, MARCA_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
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
 * HU #14562 · AC48: una TablaEditable de definición (familia o tipo de trámite) × fuentes. Cada fila guarda por su cuenta:
 * «Sin cambios», «Editado» con Descartar y Guardar, o «Error de guardado» con Reintentar y el motivo del API debajo de la
 * tabla. RUNT vehículo es una columna fija con candado: no es casilla ni viaja en el PUT.
 */

const ALCANCES_PISO = new Set(["TRASPASO", "TRASPASO_STANDARD", "TRASPASO_TRANSFERENCIA_DE_DOMINIO"]);
const SIMIT_UNILATERAL = new Set<FuenteConfigurable>(["SIMIT_VEHICULO", "SIMIT_PROPIETARIO", "SIMIT_LOCATARIO"]);
const UNILATERAL = "TRASPASO_UNILATERAL";

const MOTIVO_PISO = "El paz y salvo del propietario es obligatorio en este alcance y no se puede quitar.";
const MOTIVO_UNILATERAL = "En el traspaso unilateral no se exige el SIMIT y no se puede marcar.";

/** Casilla de una fuente en la fila: marcada y, si está bloqueada, el motivo en texto. */
interface Casilla {
  marcada: boolean;
  motivo: string | null;
}

type Fila = {
  /** Título con el nombre de la familia o del tipo (Punto 5); es también el nombre accesible de la fila. */
  titulo: string;
  familia: string | null;
  tipo_tramite_codigo: string | null;
  runt: null;
} & Record<FuenteConfigurable, Casilla>;

type Carga = { tipo: "cargando" } | { tipo: "error"; error: unknown } | { tipo: "listo"; filas: Fila[] };

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

function aFila(def: DefinicionFuentesObligatorias): Fila {
  const alcance = codigoAlcance(def);
  const crudas = (Array.isArray(def.fuentes) ? def.fuentes : []) as readonly string[];
  const presentes = FUENTES_CONFIGURABLES.filter((f) => crudas.includes(f));
  const casillas = Object.fromEntries(
    FUENTES_CONFIGURABLES.map((f) => [f, { marcada: marcada(alcance, f, presentes), motivo: motivoBloqueo(alcance, f) }]),
  ) as Record<FuenteConfigurable, Casilla>;
  return { titulo: tituloDefinicion(def), familia: def.familia, tipo_tramite_codigo: def.tipo_tramite_codigo, runt: null, ...casillas };
}

const fuentesDeFila = (fila: Fila): FuenteConfigurable[] =>
  fuentesSeleccionadas(codigoAlcance(fila), FUENTES_CONFIGURABLES.filter((f) => fila[f].marcada));

function Vacio() {
  return (
    <div className="flex flex-col items-center gap-3 py-10 text-center">
      <Icono nombre="portapapeles" className="h-10 w-10 text-flit-brand" />
      <h2 className="text-lg font-semibold">No hay definiciones guardadas</h2>
      <p className="max-w-md text-sm text-muted-foreground">Esta pantalla solo muestra las cabeceras que ya existen.</p>
    </div>
  );
}

function CasillaFuente({
  fuente,
  valor,
  etiqueta,
  onCambio,
}: {
  fuente: FuenteConfigurable;
  valor: Casilla;
  etiqueta: string;
  onCambio: (v: Casilla) => void;
}) {
  const tooltipId = `${useId()}-tooltip`;
  const { motivo } = valor;
  return (
    <span className="group relative inline-flex items-center gap-1.5">
      <input
        type="checkbox"
        value={fuente}
        aria-label={etiqueta}
        checked={valor.marcada}
        onChange={() => {
          if (!motivo) onCambio({ ...valor, marcada: !valor.marcada });
        }}
        aria-disabled={motivo ? true : undefined}
        aria-describedby={motivo ? tooltipId : undefined}
        className={MARCA_CLS}
      />
      {motivo && <Icono nombre="candado" className="h-3.5 w-3.5 text-muted-foreground" />}
      {motivo && (
        <span role="tooltip" id={tooltipId} className={TOOLTIP_CLS}>
          {motivo}
        </span>
      )}
    </span>
  );
}

const COLUMNAS: ColumnaEditable<Fila>[] = [
  {
    campo: "titulo",
    titulo: "Definición",
    mostrar: (fila) => (
      <span className="flex flex-col gap-0.5">
        <span>{fila.titulo}</span>
        {fuentesDeFila(fila).length === 0 && <span className="text-xs font-normal text-muted-foreground">Lista vacía guardada</span>}
      </span>
    ),
  },
  {
    campo: "runt",
    titulo: "RUNT vehículo",
    mostrar: () => (
      <span className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
        <span data-testid="candado-runt" className="inline-flex">
          <Icono nombre="candado" className="h-4 w-4" />
        </span>
        Siempre obligatoria
      </span>
    ),
  },
  ...FUENTES_CONFIGURABLES.map(
    (fuente): ColumnaEditable<Fila> => ({
      campo: fuente,
      titulo: nombreFuente(fuente),
      editable: true,
      editor: (valor, onCambio, etiqueta) => (
        <CasillaFuente fuente={fuente} valor={valor as Casilla} etiqueta={etiqueta} onCambio={(v) => onCambio(v as Fila[keyof Fila & string])} />
      ),
    }),
  ),
];

export function FuentesObligatoriasBackoffice() {
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  /** Motivo del último error de guardado por fila (título → mensaje); se limpia al guardar bien. */
  const [errores, setErrores] = useState<Record<string, string>>({});

  useEffect(() => {
    const control = new AbortController();
    listarFuentesObligatorias(control.signal).then(
      (lista) => {
        if (control.signal.aborted) return;
        if (!lista || !Array.isArray(lista.definiciones)) {
          setCarga({ tipo: "error", error: new Error("La respuesta no trae definiciones.") });
          return;
        }
        setCarga({ tipo: "listo", filas: lista.definiciones.map(aFila) });
      },
      (error: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error });
      },
    );
    return () => control.abort();
  }, [intento]);

  function fijarError(titulo: string, mensaje: string | null) {
    setErrores((actual) => {
      const copia = { ...actual };
      if (mensaje === null) delete copia[titulo];
      else copia[titulo] = mensaje;
      return copia;
    });
  }

  async function guardar(fila: Fila) {
    try {
      const guardada = await guardarFuentesObligatorias({
        familia: fila.familia,
        tipo_tramite_codigo: fila.tipo_tramite_codigo,
        fuentes: fuentesDeFila(fila),
      });
      const nueva = { ...aFila(guardada), titulo: fila.titulo };
      fijarError(fila.titulo, null);
      setCarga((actual) =>
        actual.tipo === "listo" ? { tipo: "listo", filas: actual.filas.map((f) => (f.titulo === fila.titulo ? nueva : f)) } : actual,
      );
    } catch (error: unknown) {
      fijarError(fila.titulo, error instanceof Error ? error.message : "No se pudo guardar esta definición.");
      throw error;
    }
  }

  let cuerpo: ReactNode;
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
  } else if (carga.filas.length === 0) {
    cuerpo = (
      <section aria-label="Definiciones" className={TARJETA_CLS}>
        <Vacio />
      </section>
    );
  } else {
    cuerpo = (
      <section aria-label="Definiciones" className={`${TARJETA_CLS} flex flex-col gap-3`}>
        <TablaEditable
          etiqueta="Fuentes obligatorias por definición"
          filas={carga.filas}
          columnas={COLUMNAS}
          clave={(f) => f.titulo}
          onGuardar={guardar}
        />
        {Object.entries(errores).map(([titulo, mensaje]) => (
          <InlineAlert key={titulo} tone="error" title={`No se guardó ${titulo}`}>
            {mensaje}
          </InlineAlert>
        ))}
      </section>
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
