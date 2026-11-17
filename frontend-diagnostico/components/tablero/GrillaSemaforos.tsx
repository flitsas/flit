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
import { enmascararDocumento } from "@/components/comun/DocumentoDeclarado";
import { nombreFuente, type Fuente } from "@/lib/configuracion";
import type { CeldaGrilla, FilaVehiculo, Grilla } from "@/lib/tablero/api";
import { rutaPaginaDetalle } from "@/lib/tablero/detalle";
import { FUENTES_TABLA, PILARES, fuentesPedidas } from "@/lib/tablero/pilares";
import { CeldaSemaforo, type CeldaSemaforoProps } from "./CeldaSemaforo";
import { nombreRol } from "@/lib/rotulos";

/** Cabecera de tabla del UI Kit 2026 (auditoría m-4): 13 px en negrita sobre la barra --table-header-*. */
const CABECERA_CLS = TABLA_HEADER_CELL_CLS;
const CABECERA_ESTILO = { background: "var(--table-header-bg)", color: "var(--table-header-fg)" } as const;
/** Separador vertical entre columnas de la cabecera. */
const SEPARADOR_CLS = "border-l border-card/60";
const TEXTO_SECUNDARIO_CLS = "text-xs text-muted-foreground";
const DOCUMENTO_CLS = "whitespace-nowrap font-mono text-xs font-medium text-flit-primary";
/** Celda de la fila; por debajo de 900 px, renglón de la tarjeta con su `data-label` (HU #14562 AC56). */
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
function RotuloConIcono({ Icono: IconoCabecera, children }: { Icono?: IconoFuente; children: ReactNode }) {
  return (
    <span className="inline-flex items-center gap-1.5">
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
  /** Acción bajo la placa (corte 7: «Dictamen»). No entra en el sondeo de la grilla: quien la pinta pide el dictamen al abrirla. */
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

/** «—» con nombre accesible cuando el dato no existe. */
function SinDato({ etiqueta }: { etiqueta: string }) {
  return (
    <span role="img" aria-label={etiqueta} className={TEXTO_SECUNDARIO_CLS}>
      —
    </span>
  );
}

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
      className={`${FIJA_IZQ_CLS} ${izquierda} bg-card px-3 py-2.5 text-left align-top font-normal ${TABLA_MOVIL_CELL_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS}`}
    >
      <span className="block font-mono text-sm font-bold uppercase tracking-wider text-flit-primary">
        {vehiculo.placa || vehiculo.vin || vehiculo.identificador}
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

/** Documento que declaró el cliente: tipo y número enmascarado (nunca completo) y su rol. */
function CeldaDocumentoDeclarado({ vehiculo }: { vehiculo: FilaVehiculo }) {
  const d = vehiculo.documento_declarado;
  return (
    <td data-columna="documento-declarado" data-label="Documento declarado" className={TD_CLS}>
      {d ? (
        <span className="flex flex-col gap-0.5">
          <span className={DOCUMENTO_CLS}>{`${d.tipo_documento} ${enmascararDocumento(d.documento_enmascarado)}`}</span>
          <span className={TEXTO_SECUNDARIO_CLS}>{nombreRol(d.rol)}</span>
        </span>
      ) : (
        <SinDato etiqueta="Sin documento declarado" />
      )}
    </td>
  );
}

/** Documentos de los propietarios del vehículo, uno por línea, enmascarados. */
function CeldaDocumentosPropietario({ vehiculo }: { vehiculo: FilaVehiculo }) {
  const propietarios = vehiculo.propietarios ?? [];
  return (
    <td data-columna="documento-propietario" data-label="Documento del propietario" className={TD_CLS}>
      {propietarios.length > 0 ? (
        <ul className="flex flex-col gap-0.5">
          {propietarios.map((p, i) => (
            <li key={`${i}-${p.documento_enmascarado}`} className={DOCUMENTO_CLS}>
              {`${p.tipo_documento} ${enmascararDocumento(p.documento_enmascarado)}`}
            </li>
          ))}
        </ul>
      ) : (
        <SinDato etiqueta="Sin documentos del propietario" />
      )}
    </td>
  );
}

/**
 * Tabla de resultados del lote (HU #13748; rediseño HU #14087), la misma para Express (1 vehículo), Masivo y backoffice. Por
 * fila: placa (o VIN) con su avance «n de m consultas» (m = fuentes pedidas) y el badge «Terminado», el documento declarado y
 * los documentos del propietario. Luego SIEMPRE las 8 fuentes agrupadas por pilar (RUNT, SIMIT, Avalúo comercial, Impuesto
 * departamental) en el orden de FUENTES: la no pedida dice «Sin solicitar»; la pedida sin celda todavía, «Sin ejecutar». El
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
    <div className="overflow-x-auto rounded-xl border border-border">
      <table aria-label="Semáforo de consultas del lote" className={`w-full min-w-[1280px] border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
        <thead className={TABLA_MOVIL_THEAD_CLS}>
          <tr>
            {casillaVehiculo && (
              <th scope="col" rowSpan={2} className={`${CABECERA_CLS} ${FIJA_IZQ_CLS} min-[900px]:left-0 w-10 align-bottom`} style={CABECERA_ESTILO}>
                <span className="sr-only">Seleccionar</span>
              </th>
            )}
            <th
              scope="col"
              rowSpan={2}
              className={`${CABECERA_CLS} ${FIJA_IZQ_CLS} ${izquierdaPlaca} ${casillaVehiculo ? SEPARADOR_CLS : ""} align-bottom`}
              style={CABECERA_ESTILO}
            >
              Placa o VIN
            </th>
            {dictamenVehiculo && (
              <th scope="col" rowSpan={2} className={`${CABECERA_CLS} ${SEPARADOR_CLS} align-bottom`} style={CABECERA_ESTILO}>
                Dictamen
              </th>
            )}
            <th scope="col" rowSpan={2} className={`${CABECERA_CLS} ${SEPARADOR_CLS} align-bottom`} style={CABECERA_ESTILO}>
              Documento declarado
            </th>
            <th scope="col" rowSpan={2} className={`${CABECERA_CLS} ${SEPARADOR_CLS} align-bottom`} style={CABECERA_ESTILO}>
              Documento del propietario
            </th>
            {PILARES.map((p) =>
              p.fuentes.length > 1 ? (
                <th key={p.nombre} scope="colgroup" colSpan={p.fuentes.length} className={`${CABECERA_CLS} ${SEPARADOR_CLS}`} style={CABECERA_ESTILO}>
                  <RotuloConIcono Icono={ICONO_PILAR[p.nombre]}>{p.nombre}</RotuloConIcono>
                </th>
              ) : (
                // Pilar de una sola fuente con su mismo nombre: una cabecera que ocupa los dos niveles, sin repetir el rótulo.
                <th key={p.nombre} scope="col" rowSpan={2} className={`${CABECERA_CLS} ${SEPARADOR_CLS} align-bottom`} style={CABECERA_ESTILO}>
                  <RotuloConIcono Icono={ICONO_PILAR[p.nombre]}>{p.nombre}</RotuloConIcono>
                </th>
              ),
            )}
            {accionVehiculo && (
              <th scope="col" rowSpan={2} className={`${CABECERA_CLS} ${FIJA_DER_CLS} ${SEPARADOR_CLS} align-bottom`} style={CABECERA_ESTILO}>
                {tituloAccionVehiculo}
              </th>
            )}
          </tr>
          <tr>
            {PILARES_AGRUPADOS.flatMap((p) => p.fuentes).map((f) => (
              <th key={f} scope="col" className={`${CABECERA_CLS} ${SEPARADOR_CLS} normal-case tracking-normal`} style={CABECERA_ESTILO}>
                <RotuloConIcono Icono={ICONO_FUENTE[f]}>{nombreFuente(f)}</RotuloConIcono>
              </th>
            ))}
          </tr>
        </thead>
        <tbody className={TABLA_MOVIL_TBODY_CLS}>
          {vehiculos.map((v) => {
            const porFuente = new Map(v.celdas.map((c) => [c.fuente, c]));
            return (
              <tr key={v.id} data-vehiculo={v.id} className={`border-t border-[var(--table-row-border)] bg-card ${TABLA_MOVIL_ROW_CLS}`}>
                {casillaVehiculo && (
                  <td data-columna="seleccion" data-label="Seleccionar" className={`${TD_CLS} ${FIJA_IZQ_CLS} min-[900px]:left-0 bg-card`}>
                    {casillaVehiculo(v)}
                  </td>
                )}
                <EncabezadoVehiculo vehiculo={v} pedidas={pedidas} accion={accionEncabezado?.(v)} izquierda={izquierdaPlaca} />
                {dictamenVehiculo && (
                  <td data-columna="dictamen" data-label="Dictamen" className={TD_CLS}>
                    {dictamenVehiculo(v)}
                  </td>
                )}
                <CeldaDocumentoDeclarado vehiculo={v} />
                <CeldaDocumentosPropietario vehiculo={v} />
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
                  <td data-label={tituloAccionVehiculo} className={`${TD_CLS} ${FIJA_DER_CLS} bg-card`}>
                    {accionVehiculo(v)}
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
