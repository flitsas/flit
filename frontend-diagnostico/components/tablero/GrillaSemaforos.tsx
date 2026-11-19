"use client";

import type { ComponentType, ReactNode } from "react";
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
import { IconoDxSimit } from "@/components/dx/iconos/IconoDxSimit";
import { IconoDxSimitPersona } from "@/components/dx/iconos/IconoDxSimitPersona";
import { RegionDesplazable } from "@/components/comun/RegionDesplazable";
import { nombreFuente, nombreSubfuente, type Fuente } from "@/lib/configuracion";
import type { CeldaGrilla, FilaVehiculo, Grilla } from "@/lib/tablero/api";
import { rutaPaginaDetalle } from "@/lib/tablero/detalle";
import { FUENTES_TABLA, PILARES, fuentesPedidas } from "@/lib/tablero/pilares";
import { CeldaSemaforo, type CeldaSemaforoProps } from "./CeldaSemaforo";
import { IconoCambioPropietario } from "./IconoCambioPropietario";
import { CONTENEDOR_TABLA_CLS, FILA_TABLA_HOVER_CLS } from "./vistasDetalle/comun";

/** Cabecera de tabla del UI Kit 2026 (auditoría m-4): 13 px en negrita sobre la barra --table-header-*. */
const CABECERA_CLS = TABLA_HEADER_CELL_CLS;
/**
 * Cabecera agrupada (RUNT, SIMIT) centrada: se **sustituye** el text-left del kit. Añadir text-center detrás no basta,
 * porque con las dos utilidades en la misma celda gana la que Tailwind emite después en el CSS (text-left), no la del
 * final del string (E2E acciones-detalle:68, spec-ui §2.6).
 */
const CABECERA_GRUPO_CLS = TABLA_HEADER_CELL_CLS.replace(/\btext-left\b/, "text-center");
const CABECERA_ESTILO = { background: "var(--table-header-bg)", color: "var(--table-header-fg)" } as const;
/** Separador vertical entre columnas de la cabecera. */
const SEPARADOR_CLS = "border-l border-card/60";
const TEXTO_SECUNDARIO_CLS = "text-xs text-muted-foreground";
/** Celda de la fila; por debajo de 1024 px, renglón de la tarjeta con su `data-label` (HU #14562 AC56). */
const TD_CLS = `px-3 py-2.5 align-top ${TABLA_MOVIL_CELL_CLS}`;

type IconoFuente = ComponentType<{ className?: string; tamano?: 16 | 20 | 24 }>;

/** HU #14562 AC28 (auditoría M7): ícono de la familia IconoDx de cada pilar y de cada fuente en la cabecera. */
const ICONO_PILAR: Readonly<Record<string, IconoFuente>> = {
  RUNT: IconoDxRunt,
  SIMIT: IconoDxSimit,
  "Avalúo comercial": IconoDxAvaluo,
  "Impuesto departamental": IconoDxImpuesto,
};
const ICONO_FUENTE: Readonly<Record<Fuente, IconoFuente>> = {
  RUNT: IconoDxRunt,
  RUNT_PROPIETARIO: IconoDxRuntPersona,
  RUNT_LOCATARIO: IconoDxLocatario,
  SIMIT_VEHICULO: IconoDxSimit,
  SIMIT_PROPIETARIO: IconoDxSimitPersona,
  SIMIT_LOCATARIO: IconoDxLocatario,
  AVALUO: IconoDxAvaluo,
  IMPUESTO_DEPARTAMENTAL: IconoDxImpuesto,
};

/** Rótulo de cabecera con su ícono decorativo de 16 px: el nombre accesible sigue siendo el texto. */
function RotuloConIcono({ Icono: IconoCabecera, centrado = false, children }: { Icono?: IconoFuente; centrado?: boolean; children: ReactNode }) {
  return (
    <span className={`inline-flex items-center gap-1.5${centrado ? " justify-center" : ""}`}>
      {IconoCabecera ? <IconoCabecera tamano={16} className="shrink-0" /> : null}
      {children}
    </span>
  );
}

const RESUELTAS: ReadonlySet<CeldaGrilla["estado"]> = new Set(["COMPLETADO", "NA", "ERROR"]);


/** Pilares con más de una fuente: su cabecera agrupa (colgroup) y sus fuentes van en el segundo nivel. */
const PILARES_AGRUPADOS = PILARES.filter((p) => p.fuentes.length > 1);

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
 * HU #14562 AC28: placa y trámite fijos al desplazar en horizontal, solo desde 1024 px (auditoría B2). Por debajo, la fila es
 * una tarjeta (AC56) y nada queda fijo: a 360 px las columnas fijas tapaban todas las fuentes.
 */
const FIJA_IZQ_CLS = "lg:sticky z-10";
const FIJA_DER_CLS = "lg:sticky lg:right-0 z-10";
/**
 * M-2 (A6.2): la th de una columna fija es sticky arriba y al costado. Va en z-20, por encima de las th sticky normales (z-10 del
 * kit), que si no la tapan al desplazar en horizontal porque van después en el DOM. Las celdas fijas del cuerpo siguen en z-10.
 */
const CABECERA_FIJA_CLS = `${CABECERA_CLS.replace(/\bz-10\b/, "z-20")} lg:sticky`;
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
 * el badge «Terminado»; sin columnas de documento (contrato 1.6.0 C5, SP-4/SP-19). Luego SIEMPRE las 8 fuentes agrupadas por pilar (RUNT, SIMIT, Avalúo comercial, Impuesto
 * departamental) en el orden de FUENTES: la no pedida dice «Sin solicitar»; la pedida sin celda todavía, «Sin ejecutar». El
 * usuario no ve acciones; el admin, «Reprocesar» solo en las rojas. Cada semáforo con consulta abre su detalle (HU #13801)
 * en una pestaña nueva (HU #14087), ya sin modal.
 * El desborde horizontal queda dentro del contenedor de la tabla, nunca en la página. Por debajo de 1024 px cada vehículo es
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
  // Con casilla de selección, la placa queda fija a su derecha (la casilla mide w-10). Solo desde 1024 px, como el sticky.
  const izquierdaPlaca = casillaVehiculo ? "lg:left-10" : "lg:left-0";

  return (
    // Ronda 2 de E2E: mismo patrón que el resto de contenedores desplazables de la ola (región enfocable solo con desborde).
    <RegionDesplazable nombre="Semáforo de consultas del lote" className={CONTENEDOR_TABLA_CLS}>
      <table aria-label="Semáforo de consultas del lote" className={`w-full min-w-[1040px] border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
        <thead className={TABLA_MOVIL_THEAD_CLS}>
          <tr>
            {casillaVehiculo && (
              <th scope="col" rowSpan={2} className={`${CABECERA_FIJA_CLS} lg:left-0 w-10 align-bottom`} style={CABECERA_ESTILO}>
                <span className="sr-only">Seleccionar</span>
              </th>
            )}
            <th
              scope="col"
              rowSpan={2}
              className={`${CABECERA_FIJA_CLS} ${izquierdaPlaca} ${casillaVehiculo ? SEPARADOR_CLS : ""} align-bottom`}
              style={CABECERA_ESTILO}
            >
              Placa o VIN
            </th>
            {dictamenVehiculo && (
              <th scope="col" rowSpan={2} className={`${CABECERA_CLS} ${SEPARADOR_CLS} align-bottom`} style={CABECERA_ESTILO}>
                Dictamen
              </th>
            )}
            {PILARES.map((p) =>
              p.fuentes.length > 1 ? (
                // FE-2 (spec-ui §2.6): «RUNT» y «SIMIT» centrados sobre sus subcolumnas (sin el text-left del kit).
                <th key={p.nombre} scope="colgroup" colSpan={p.fuentes.length} className={`${CABECERA_GRUPO_CLS} ${SEPARADOR_CLS}`} style={CABECERA_ESTILO}>
                  <RotuloConIcono Icono={ICONO_PILAR[p.nombre]} centrado>
                    {p.nombre}
                  </RotuloConIcono>
                </th>
              ) : (
                // Pilar de una sola fuente con su mismo nombre: una cabecera que ocupa los dos niveles, sin repetir el rótulo.
                <th key={p.nombre} scope="col" rowSpan={2} className={`${CABECERA_CLS} ${SEPARADOR_CLS} align-bottom`} style={CABECERA_ESTILO}>
                  <RotuloConIcono Icono={ICONO_PILAR[p.nombre]}>{p.nombre}</RotuloConIcono>
                </th>
              ),
            )}
            {accionVehiculo && (
              <th scope="col" rowSpan={2} className={`${CABECERA_FIJA_CLS} lg:right-0 ${SEPARADOR_CLS} ${ANCHO_ACCIONES_CLS} align-bottom`} style={CABECERA_ESTILO}>
                {tituloAccionVehiculo}
              </th>
            )}
          </tr>
          <tr>
            {PILARES_AGRUPADOS.flatMap((p) => p.fuentes).map((f) => (
              // aria-label: el nombre completo («RUNT propietario»); el cálculo del nombre recorta el espacio del sr-only.
              <th key={f} scope="col" aria-label={nombreFuente(f)} className={`${CABECERA_CLS} ${SEPARADOR_CLS} normal-case tracking-normal`} style={CABECERA_ESTILO}>
                {/* Texto visible sin el prefijo del grupo; el nombre completo para lectores (scope=colgroup se anuncia desigual). */}
                <RotuloConIcono Icono={ICONO_FUENTE[f]}>
                  <span>
                    <span className="sr-only">{nombreFuente(f).slice(0, -nombreSubfuente(f).length)}</span>
                    {nombreSubfuente(f)}
                  </span>
                </RotuloConIcono>
              </th>
            ))}
          </tr>
        </thead>
        <tbody className={TABLA_MOVIL_TBODY_CLS}>
          {vehiculos.map((v) => {
            const porFuente = new Map(v.celdas.map((c) => [c.fuente, c]));
            return (
              <tr key={v.id} data-vehiculo={v.id} className={`group border-t border-[var(--table-row-border)] ${FILA_TABLA_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS}`}>
                {casillaVehiculo && (
                  <td data-columna="seleccion" data-label="Seleccionar" className={`${TD_CLS} ${FIJA_IZQ_CLS} lg:left-0 ${FONDO_FIJA_CLS}`}>
                    {casillaVehiculo(v)}
                  </td>
                )}
                <EncabezadoVehiculo vehiculo={v} pedidas={pedidas} accion={accionEncabezado?.(v)} izquierda={izquierdaPlaca} />
                {dictamenVehiculo && (
                  <td data-columna="dictamen" data-label="Dictamen" className={TD_CLS}>
                    {dictamenVehiculo(v)}
                  </td>
                )}
                {FUENTES_TABLA.map((f) => {
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
                    />
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
