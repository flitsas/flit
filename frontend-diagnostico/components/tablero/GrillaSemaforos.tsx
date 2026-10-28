"use client";

import type { ReactNode } from "react";
import { CheckCircle2 } from "lucide-react";
import { enmascararDocumento } from "@/components/comun/DocumentoDeclarado";
import { nombreFuente, type Fuente } from "@/lib/configuracion";
import type { CeldaGrilla, FilaVehiculo, Grilla } from "@/lib/tablero/api";
import { rutaPaginaDetalle } from "@/lib/tablero/detalle";
import { FUENTES_TABLA, PILARES, fuentesPedidas } from "@/lib/tablero/pilares";
import { CeldaSemaforo, type CeldaSemaforoProps } from "./CeldaSemaforo";

/** Cabecera de tabla del sistema: barra gris de marca (--table-header-*), versalita de 10px. */
const CABECERA_CLS = "px-3 py-2.5 text-left text-[10px] font-semibold uppercase tracking-wider";
const CABECERA_ESTILO = { background: "var(--table-header-bg)", color: "var(--table-header-fg)" } as const;
/** Separador vertical entre columnas de la cabecera. */
const SEPARADOR_CLS = "border-l border-white/60 dark:border-white/10";
const TEXTO_SECUNDARIO_CLS = "text-xs text-[var(--nav-texto)] dark:text-slate-300";
const DOCUMENTO_CLS = "whitespace-nowrap font-mono text-xs font-medium text-flit-primary dark:text-white";
const TD_CLS = "px-3 py-2.5 align-top";

const RESUELTAS: ReadonlySet<CeldaGrilla["estado"]> = new Set(["COMPLETADO", "NA", "ERROR"]);

const ROLES: Record<string, string> = { PROPIETARIO: "Propietario", LOCATARIO: "Locatario" };

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
}

/** «—» con nombre accesible cuando el dato no existe. */
function SinDato({ etiqueta }: { etiqueta: string }) {
  return (
    <span role="img" aria-label={etiqueta} className={TEXTO_SECUNDARIO_CLS}>
      —
    </span>
  );
}

function EncabezadoVehiculo({ vehiculo, pedidas }: { vehiculo: FilaVehiculo; pedidas: ReadonlySet<Fuente> }) {
  const resueltas = vehiculo.celdas.filter((c) => pedidas.has(c.fuente) && RESUELTAS.has(c.estado)).length;
  return (
    <th scope="row" className="px-3 py-2.5 text-left align-top font-normal">
      <span className="block font-mono text-sm font-bold uppercase tracking-wider text-flit-primary dark:text-white">
        {vehiculo.placa || vehiculo.vin || vehiculo.identificador}
      </span>
      <span className={`mt-1 block ${TEXTO_SECUNDARIO_CLS}`}>{`${resueltas} de ${pedidas.size} consultas`}</span>
      {vehiculo.terminado && (
        <span
          className="mt-1 inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-semibold"
          style={{ background: "var(--badge-success-bg)", color: "var(--badge-success-fg)", borderColor: "var(--badge-success-border)" }}
        >
          <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
          Terminado
        </span>
      )}
    </th>
  );
}

/** Documento que declaró el cliente: tipo y número enmascarado (nunca completo) y su rol. */
function CeldaDocumentoDeclarado({ vehiculo }: { vehiculo: FilaVehiculo }) {
  const d = vehiculo.documento_declarado;
  return (
    <td data-columna="documento-declarado" className={TD_CLS}>
      {d ? (
        <span className="flex flex-col gap-0.5">
          <span className={DOCUMENTO_CLS}>{`${d.tipo_documento} ${enmascararDocumento(d.documento_enmascarado)}`}</span>
          <span className={TEXTO_SECUNDARIO_CLS}>{ROLES[d.rol] ?? d.rol}</span>
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
    <td data-columna="documento-propietario" className={TD_CLS}>
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
 * El desborde horizontal queda dentro del contenedor de la tabla, nunca en la página.
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
}: GrillaSemaforosProps) {
  const pedidas = fuentesPedidas(grilla.columnas);

  return (
    <div className="overflow-x-auto rounded-xl border border-[var(--color-flit-gray)] dark:border-white/10">
      <table aria-label="Semáforo de consultas del lote" className="w-full min-w-[1280px] border-collapse text-sm">
        <thead>
          <tr>
            <th scope="col" rowSpan={2} className={`${CABECERA_CLS} align-bottom`} style={CABECERA_ESTILO}>
              Placa o VIN
            </th>
            <th scope="col" rowSpan={2} className={`${CABECERA_CLS} ${SEPARADOR_CLS} align-bottom`} style={CABECERA_ESTILO}>
              Documento declarado
            </th>
            <th scope="col" rowSpan={2} className={`${CABECERA_CLS} ${SEPARADOR_CLS} align-bottom`} style={CABECERA_ESTILO}>
              Documento del propietario
            </th>
            {PILARES.map((p) =>
              p.fuentes.length > 1 ? (
                <th key={p.nombre} scope="colgroup" colSpan={p.fuentes.length} className={`${CABECERA_CLS} ${SEPARADOR_CLS}`} style={CABECERA_ESTILO}>
                  {p.nombre}
                </th>
              ) : (
                // Pilar de una sola fuente con su mismo nombre: una cabecera que ocupa los dos niveles, sin repetir el rótulo.
                <th key={p.nombre} scope="col" rowSpan={2} className={`${CABECERA_CLS} ${SEPARADOR_CLS} align-bottom`} style={CABECERA_ESTILO}>
                  {p.nombre}
                </th>
              ),
            )}
            {accionVehiculo && (
              <th scope="col" rowSpan={2} className={`${CABECERA_CLS} ${SEPARADOR_CLS} align-bottom`} style={CABECERA_ESTILO}>
                Reporte
              </th>
            )}
          </tr>
          <tr>
            {PILARES_AGRUPADOS.flatMap((p) => p.fuentes).map((f) => (
              <th key={f} scope="col" className={`${CABECERA_CLS} ${SEPARADOR_CLS} normal-case tracking-normal`} style={CABECERA_ESTILO}>
                {nombreFuente(f)}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {grilla.vehiculos.map((v) => {
            const porFuente = new Map(v.celdas.map((c) => [c.fuente, c]));
            return (
              <tr key={v.id} data-vehiculo={v.id} className="border-t border-[var(--table-row-border)] bg-white dark:bg-transparent">
                <EncabezadoVehiculo vehiculo={v} pedidas={pedidas} />
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
                {accionVehiculo && <td className={TD_CLS}>{accionVehiculo(v)}</td>}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
