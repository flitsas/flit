"use client";

import { Fragment, type ComponentType, type ReactNode } from "react";
import { Icono } from "@flit/ui/Icono";
import { etiquetaKit } from "@flit/ui/StatusBadge";
import {
  TABLA_HEADER_CELL_CLS,
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import { IconoDxAvaluo } from "@/components/dx/iconos/IconoDxAvaluo";
import { IconoDxImpuesto } from "@/components/dx/iconos/IconoDxImpuesto";
import { IconoDxLocatario } from "@/components/dx/iconos/IconoDxLocatario";
import { IconoDxRunt } from "@/components/dx/iconos/IconoDxRunt";
import { IconoDxRuntPersona } from "@/components/dx/iconos/IconoDxRuntPersona";
import { RegionDesplazable } from "@/components/comun/RegionDesplazable";
import { nombreFuente, type Fuente } from "@/lib/configuracion";
import type { CeldaGrilla, FilaVehiculo, Grilla } from "@/lib/tablero/api";
import { rutaPaginaDetalle } from "@/lib/tablero/detalle";
import { fuentesPedidas } from "@/lib/tablero/pilares";
import { CeldaSemaforo, type CeldaSemaforoProps } from "./CeldaSemaforo";
import { IconoCambioPropietario } from "./IconoCambioPropietario";
import { CONTENEDOR_TABLA_CLS, FILA_TABLA_HOVER_CLS } from "./vistasDetalle/comun";

/** Cabecera de tabla del UI Kit 2026 (auditoría m-4): 13 px en negrita sobre la barra --table-header-*. */
const CABECERA_CLS = TABLA_HEADER_CELL_CLS;
const CABECERA_ESTILO = { background: "var(--table-header-bg)", color: "var(--table-header-fg)" } as const;
/** Separador vertical entre columnas de la cabecera. */
const SEPARADOR_CLS = "border-l border-card/60";
const TEXTO_SECUNDARIO_CLS = "text-xs text-muted-foreground";
/** Celda de la fila; por debajo de 900 px, renglón de la tarjeta con su `data-label` (HU #14562 AC56). */
const TD_CLS = `px-3 py-2.5 align-top ${TABLA_MOVIL_CELL_CLS}`;

type IconoFuente = ComponentType<{ className?: string; tamano?: 16 | 20 | 24 }>;

/**
 * Columnas de fuentes de la tabla. Vehículo, Propietario y Locatario son una columna por sujeto con dos badges apilados, uno
 * por consulta («RUNT · <estado>» y «SIMIT · <estado>»), cada uno con su tooltip y su enlace al detalle de su fuente; sin las
 * cabeceras de grupo «RUNT» y «SIMIT». Avalúo comercial e Impuesto departamental siguen con una fuente por columna. El ícono
 * del sujeto va a la izquierda del rótulo (HU #14562 AC28: familia IconoDx).
 */
interface ColumnaFuentes {
  rotulo: string;
  Icono: IconoFuente;
  fuentes: readonly { fuente: Fuente; prefijo?: string }[];
}
const COLUMNAS: readonly ColumnaFuentes[] = [
  { rotulo: "Vehículo", Icono: IconoDxRunt, fuentes: [{ fuente: "RUNT", prefijo: "RUNT" }, { fuente: "SIMIT_VEHICULO", prefijo: "SIMIT" }] },
  { rotulo: "Propietario", Icono: IconoDxRuntPersona, fuentes: [{ fuente: "RUNT_PROPIETARIO", prefijo: "RUNT" }, { fuente: "SIMIT_PROPIETARIO", prefijo: "SIMIT" }] },
  { rotulo: "Locatario", Icono: IconoDxLocatario, fuentes: [{ fuente: "RUNT_LOCATARIO", prefijo: "RUNT" }, { fuente: "SIMIT_LOCATARIO", prefijo: "SIMIT" }] },
  { rotulo: "Avalúo comercial", Icono: IconoDxAvaluo, fuentes: [{ fuente: "AVALUO" }] },
  { rotulo: "Impuesto departamental", Icono: IconoDxImpuesto, fuentes: [{ fuente: "IMPUESTO_DEPARTAMENTAL" }] },
];

/** Rótulo de cabecera con su ícono decorativo de 16 px: el nombre accesible sigue siendo el texto. */
function RotuloConIcono({ Icono: IconoCabecera, children }: { Icono?: IconoFuente; children: ReactNode }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      {IconoCabecera ? <IconoCabecera tamano={16} className="shrink-0" /> : null}
      {children}
    </span>
  );
}

const RESUELTAS: ReadonlySet<CeldaGrilla["estado"]> = new Set(["COMPLETADO", "NA", "ERROR"]);


export interface GrillaSemaforosProps extends Pick<CeldaSemaforoProps, "onReprocesada" | "onErrorReproceso"> {
  grilla: Grilla;
  /** Capacidad `reprocesarConsultas` de la sesión (admin): solo entonces hay «Reprocesar» en las celdas rojas. */
  puedeReprocesar: boolean;
  /**
   * Página del detalle de una celda, que el semáforo abre en una pestaña nueva (HU #14087). Por defecto, la de la compañía
   * (/diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente}); el backoffice pasa la suya con `?empresa=`.
   */
  hrefDetalle?: (celda: CeldaGrilla, vehiculo: FilaVehiculo) => string;
  /** Acción extra bajo el semáforo de una celda (p. ej. «Llenar a mano» en el backoffice, que abre su modal). */
  accionCelda?: (celda: CeldaGrilla, vehiculo: FilaVehiculo) => ReactNode;
  /** Marca bajo el semáforo de cada celda: solo el backoffice (origen manual, HU #13802); la compañía nunca (Q5). */
  marcaCelda?: (celda: CeldaGrilla) => ReactNode;
  /**
   * Acción al final de cada fila en la columna «Reporte» (HU #13851: «PDF» del vehículo, solo con la capacidad
   * `descargarReportes`). Sin ella la columna no existe (backoffice y compañía sin la capacidad).
   */
  accionVehiculo?: (vehiculo: FilaVehiculo) => ReactNode;
  /** Título de la columna de `accionVehiculo`; por defecto «Reporte» (Punto 3: «Acciones» cuando también hay reintentos). */
  tituloAccionVehiculo?: string;
  /**
   * Marcas bajo la placa (backoffice: «Ajustado por FLIT», etiqueta del saneamiento). HU #14584 §6.4: Dictamen y Auditoría ya
   * no van aquí sino en el menú «Acciones» (`accionVehiculo`).
   */
  accionEncabezado?: (vehiculo: FilaVehiculo) => ReactNode;
  /**
   * Casilla de selección al inicio de cada fila (Punto 4, masivo: solo con la capacidad `derivar` y la derivación encendida).
   * Sin ella la columna no existe.
   */
  casillaVehiculo?: (vehiculo: FilaVehiculo) => ReactNode;
  /**
   * HU #14562 AC28: columna «Dictamen» tras la placa (Viable / Bloqueado / Pendiente, de `FilaVehiculo.veredicto`). Sin ella la
   * columna no existe (backoffice, que abre el dictamen bajo la placa).
   */
  dictamenVehiculo?: (vehiculo: FilaVehiculo) => ReactNode;
  /** Filas a pintar (filtro de tarjeta o búsqueda). Por defecto, todas las de la grilla. */
  vehiculos?: FilaVehiculo[];
}

/**
 * HU #14562 AC28: placa y trámite fijos al desplazar en horizontal, solo desde 900 px (auditoría B2). Por debajo, la fila es
 * una tarjeta (AC56) y nada queda fijo: a 360 px las columnas fijas tapaban todas las fuentes.
 */
const FIJA_IZQ_CLS = "min-[900px]:sticky z-10";
const FIJA_DER_CLS = "min-[900px]:sticky min-[900px]:right-0 z-10";
/**
 * M-2 (A6.2): la th de una columna fija es sticky arriba y al costado. Va en z-20, por encima de las th sticky normales (z-10 del
 * kit), que si no la tapan al desplazar en horizontal porque van después en el DOM. Las celdas fijas del cuerpo siguen en z-10.
 */
const CABECERA_FIJA_CLS = `${CABECERA_CLS.replace(/\bz-10\b/, "z-20")} min-[900px]:sticky`;
/** FE-2 (spec-ui §2.3): la columna de acciones (chip + disparador «Acciones» + mensaje) mide 10rem en th y td. */
const ANCHO_ACCIONES_CLS = "w-[10rem] min-w-[10rem]";
/** CR-6: las celdas fijas tienen fondo propio (tapan lo que desplaza debajo); con la fila en hover toman su mismo color. */
const FONDO_FIJA_CLS = "bg-card group-hover:bg-[var(--table-row-hover)] transition-colors duration-[var(--dx-motion-rapido)] motion-reduce:transition-none";

function EncabezadoVehiculo({
  vehiculo,
  pedidas,
  accion,
  izquierda,
}: {
  vehiculo: FilaVehiculo;
  pedidas: ReadonlySet<Fuente>;
  accion?: ReactNode;
  izquierda: string;
}) {
  const resueltas = vehiculo.celdas.filter((c) => pedidas.has(c.fuente) && RESUELTAS.has(c.estado)).length;
  return (
    <th
      scope="row"
      data-label="Placa o VIN"
      className={`${FIJA_IZQ_CLS} ${izquierda} ${FONDO_FIJA_CLS} px-3 py-2.5 text-left align-top font-normal ${TABLA_MOVIL_CELL_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS}`}
    >
      {/* HU #14585 (spec-ui §6.5): el ícono de cambio de propietario a la derecha de la placa, en la misma línea. */}
      <span className="flex flex-wrap items-center gap-1.5">
        <span className="font-mono text-sm font-bold uppercase tracking-wider text-flit-primary">
          {vehiculo.placa || vehiculo.vin || vehiculo.identificador}
        </span>
        <IconoCambioPropietario cambio={vehiculo.cambio_propietario} />
      </span>
      <span className={`mt-1 block ${TEXTO_SECUNDARIO_CLS}`}>{`${resueltas} de ${pedidas.size} consultas`}</span>
      {vehiculo.terminado && (
        <span {...etiquetaKit("success", { className: "mt-1" })}>
          <Icono nombre="check" className="h-3.5 w-3.5" />
          Terminado
        </span>
      )}
      {accion ? <div className="mt-2">{accion}</div> : null}
    </th>
  );
}

/**
 * Tabla de resultados del lote (HU #13748; rediseño HU #14087), la misma para Express (1 vehículo), Masivo y backoffice. Por
 * fila: placa (o VIN) con el ícono de cambio de propietario (HU #14585), su avance «n de m consultas» (m = fuentes pedidas) y
 * el badge «Terminado»; sin columnas de documento (contrato 1.6.0 C5, SP-4/SP-19). Luego SIEMPRE las 8 fuentes: Vehículo, Propietario y Locatario
 * (dos badges por sujeto, RUNT y SIMIT), Avalúo comercial e Impuesto departamental: la no pedida dice «Sin solicitar»; la pedida sin celda todavía, «Sin ejecutar». El
 * usuario no ve acciones; el admin, «Reprocesar» solo en las rojas. Cada semáforo con consulta abre su detalle (HU #13801)
 * en una pestaña nueva (HU #14087), ya sin modal.
 * El desborde horizontal queda dentro del contenedor de la tabla, nunca en la página. Por debajo de 900 px cada vehículo es
 * una tarjeta con las recetas TABLA_MOVIL_* (HU #14562 AC56): la placa de título y un renglón por columna con su rótulo.
 */
export function GrillaSemaforos({
  grilla,
  puedeReprocesar,
  onReprocesada,
  onErrorReproceso,
  hrefDetalle,
  accionCelda,
  marcaCelda,
  accionVehiculo,
  tituloAccionVehiculo = "Reporte",
  accionEncabezado,
  casillaVehiculo,
  dictamenVehiculo,
  vehiculos = grilla.vehiculos,
}: GrillaSemaforosProps) {
  const pedidas = fuentesPedidas(grilla.columnas);
  // Con casilla de selección, la placa queda fija a su derecha (la casilla mide w-10). Solo desde 900 px, como el sticky.
  const izquierdaPlaca = casillaVehiculo ? "min-[900px]:left-10" : "min-[900px]:left-0";

  return (
    // Ronda 2 de E2E: mismo patrón que el resto de contenedores desplazables de la ola (región enfocable solo con desborde).
    <RegionDesplazable nombre="Semáforo de consultas del lote" className={CONTENEDOR_TABLA_CLS}>
      <table aria-label="Semáforo de consultas del lote" className={`w-full min-w-[1040px] border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
        <thead className={TABLA_MOVIL_THEAD_CLS}>
          <tr>
            {casillaVehiculo && (
              <th scope="col" className={`${CABECERA_FIJA_CLS} min-[900px]:left-0 w-10`} style={CABECERA_ESTILO}>
                <span className="sr-only">Seleccionar</span>
              </th>
            )}
            <th
              scope="col"
             
              className={`${CABECERA_FIJA_CLS} ${izquierdaPlaca} ${casillaVehiculo ? SEPARADOR_CLS : ""}`}
              style={CABECERA_ESTILO}
            >
              Placa o VIN
            </th>
            {dictamenVehiculo && (
              <th scope="col" className={`${CABECERA_CLS} ${SEPARADOR_CLS}`} style={CABECERA_ESTILO}>
                Dictamen
              </th>
            )}
            {COLUMNAS.map((col) => (
              <th key={col.rotulo} scope="col" className={`${CABECERA_CLS} ${SEPARADOR_CLS}`} style={CABECERA_ESTILO}>
                <RotuloConIcono Icono={col.Icono}>{col.rotulo}</RotuloConIcono>
              </th>
            ))}
            {accionVehiculo && (
              <th scope="col" className={`${CABECERA_FIJA_CLS} min-[900px]:right-0 ${SEPARADOR_CLS} ${ANCHO_ACCIONES_CLS}`} style={CABECERA_ESTILO}>
                {tituloAccionVehiculo}
              </th>
            )}
          </tr>
        </thead>
        <tbody className={TABLA_MOVIL_TBODY_CLS}>
          {vehiculos.map((v) => {
            const porFuente = new Map(v.celdas.map((c) => [c.fuente, c]));
            return (
              <tr key={v.id} data-vehiculo={v.id} className={`group border-t border-[var(--table-row-border)] ${FILA_TABLA_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS}`}>
                {casillaVehiculo && (
                  <td data-columna="seleccion" data-label="Seleccionar" className={`${TD_CLS} ${FIJA_IZQ_CLS} min-[900px]:left-0 ${FONDO_FIJA_CLS}`}>
                    {casillaVehiculo(v)}
                  </td>
                )}
                <EncabezadoVehiculo vehiculo={v} pedidas={pedidas} accion={accionEncabezado?.(v)} izquierda={izquierdaPlaca} />
                {dictamenVehiculo && (
                  <td data-columna="dictamen" data-label="Dictamen" className={TD_CLS}>
                    {dictamenVehiculo(v)}
                  </td>
                )}
                {COLUMNAS.map((col) => {
                  const celdas = col.fuentes.map(({ fuente: f, prefijo }) => {
                    const solicitada = pedidas.has(f);
                    const celda = solicitada ? porFuente.get(f) : undefined;
                    return (
                      <CeldaSemaforo
                        key={f}
                        fuente={f}
                        celda={celda}
                        solicitada={solicitada}
                        nombreFuente={nombreFuente(f)}
                        identificador={v.identificador}
                        puedeReprocesar={puedeReprocesar}
                        onReprocesada={onReprocesada}
                        onErrorReproceso={onErrorReproceso}
                        hrefDetalle={celda ? (hrefDetalle?.(celda, v) ?? rutaPaginaDetalle(grilla.id, v.id, celda.fuente)) : undefined}
                        marca={celda && marcaCelda ? marcaCelda(celda) : undefined}
                        accion={celda && accionCelda ? accionCelda(celda, v) : undefined}
                        prefijo={prefijo}
                      />
                    );
                  });
                  // Sujeto (dos consultas): una td con los dos badges apilados; fuente única: la td es la de CeldaSemaforo.
                  return col.fuentes.length > 1 ? (
                    <td key={col.rotulo} data-columna={col.rotulo} data-label={col.rotulo} className={TD_CLS}>
                      <div className="flex flex-col items-start gap-1.5 max-[900px]:items-end">{celdas}</div>
                    </td>
                  ) : (
                    <Fragment key={col.rotulo}>{celdas}</Fragment>
                  );
                })}
                {accionVehiculo && (
                  <td data-label={tituloAccionVehiculo} className={`${TD_CLS} ${FIJA_DER_CLS} ${ANCHO_ACCIONES_CLS} ${FONDO_FIJA_CLS}`}>
                    {accionVehiculo(v)}
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>
    </RegionDesplazable>
  );
}
