"use client";

import { useState, type ReactNode } from "react";
import { SelectorSegmentado } from "@flit/ui/SelectorSegmentado";
import type { StatusTone } from "@flit/ui/StatusBadge";
import { TablaEditable, type ColumnaEditable } from "@flit/ui/TablaEditable";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import { AYUDA_CLS, CAMPO_CLS, ENLACE_ACCION_CLS } from "@/components/configuracion/estilos";
import { RegionDesplazable } from "@/components/comun/RegionDesplazable";
import { EtiquetaTono } from "@/components/saneamiento/SeccionesDeuda";
import { CABECERA_TABLA_CLS, CABECERA_TABLA_ESTILO, CELDA_TABLA_CLS, CONTENEDOR_TABLA_CLS, FILA_TABLA_CLS, FILA_TABLA_HOVER_CLS } from "@/components/tablero/vistasDetalle/comun";
import type { RenglonSaneamiento, ResultadoRenglon } from "@/lib/saneamiento/api";
import { ROTULO_HALLAZGO, ROTULO_RESULTADO, rotuloConcepto } from "@/lib/saneamiento/estados";
import { LIMITES_TEXTO } from "@/lib/saneamiento/textoLibre";

// Renglones de la gestión (spec-ui §6.3; HU #14577 AC31–AC33 y AC35). En EN_GESTION, `TablaEditable` en guardado
// automático (FE-0, §8): el resultado guarda 600 ms tras el último cambio y la nota al salir del campo; cada fila guarda
// sola, con su versión, y conserva lo digitado si falla. La evidencia no pasa por el borrador: se sube en el acto (D-05).
// En SANEADO y NO_PROCEDE, la misma información en solo lectura.
// Hallazgo E2E H-10: la tabla vive en la columna derecha del diseño a dos columnas (≈680 px a 1280 px de pantalla) y no
// cabe como tabla; pasa a tarjetas por el ancho de su contenedor (`apilado="contenedor"`), con el segmentado en una sola
// línea y la nota de al menos 12rem. Solo si la tarjeta es más angosta que el segmentado (≈23rem, un celular) se parte.


const OPCIONES_RESULTADO: { valor: ResultadoRenglon; label: string }[] = [
  { valor: "PENDIENTE", label: ROTULO_RESULTADO.PENDIENTE },
  { valor: "SANEADO", label: ROTULO_RESULTADO.SANEADO },
  { valor: "NO_PROCEDE", label: ROTULO_RESULTADO.NO_PROCEDE },
];

/**
 * H-10: el segmentado del kit en una sola línea, como en el resto de la suite. Solo se parte si su contenedor (el envoltorio
 * de la tabla) mide menos de 23rem, donde las tres opciones no caben en horizontal ni a todo el ancho de la tarjeta.
 */
export const SEGMENTADO_EN_LINEA_CLS = "flex-nowrap @max-[23rem]:flex-wrap";
/** H-10: la nota nunca baja de 12rem; en la tarjeta ocupa el resto de la línea o la línea entera. */
export const NOTA_CLS = "flex min-w-[12rem] flex-1 flex-col gap-1";

const TONO_RESULTADO: Record<ResultadoRenglon, StatusTone> = { PENDIENTE: "neutral", SANEADO: "success", NO_PROCEDE: "neutral" };

/** «12 KB» / «1,5 MB». */
export function formatoTamano(bytes: number): string {
  if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toLocaleString("es-CO", { maximumFractionDigits: 1 })} MB`;
  if (bytes >= 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${bytes} bytes`;
}

/** Fila de la tabla: el renglón más `quitar`, la clave de la columna de acción (la tabla pide un campo por columna). */
type Fila = RenglonSaneamiento & { quitar: string };

/** Acciones que la tabla le pide a la gestión. */
export interface AccionesRenglon {
  /** Guarda resultado y nota del renglón (PUT …/renglones/{rid}); rechaza con el motivo para la fila. */
  guardar: (r: RenglonSaneamiento) => Promise<void>;
  /** El renglón cambió en el cliente (o volvió a guardarse): para la regla «Espera a que se guarden los cambios». */
  alEditar: (rid: string, sucio: boolean) => void;
  cargarEvidencia: (r: RenglonSaneamiento) => void;
  verEvidencia: (r: RenglonSaneamiento) => void;
  quitarEvidencia: (r: RenglonSaneamiento) => void;
  quitarRenglon: (r: RenglonSaneamiento) => void;
}

export interface RenglonesEditablesProps {
  renglones: RenglonSaneamiento[];
  acciones: AccionesRenglon;
  /** Motivo del último error de cada fila (409, red…). */
  errores: Readonly<Record<string, string>>;
  /** Descarga en curso o su error, por renglón. */
  descargas: Readonly<Record<string, "descargando" | string>>;
}

function Contador({ id, largo }: { id: string; largo: number }) {
  return (
    <span id={id} className={largo >= 450 ? `${AYUDA_CLS} tabular-nums` : "sr-only"}>
      {largo >= 450 ? `${largo}/${LIMITES_TEXTO.nota.max}` : ""}
    </span>
  );
}

function CeldaEvidencia({ r, acciones, descarga }: { r: RenglonSaneamiento; acciones: AccionesRenglon; descarga?: string }) {
  const concepto = rotuloConcepto(r);
  if (!r.evidencia) {
    return (
      <button type="button" aria-label={`Cargar evidencia de ${concepto}`} onClick={() => acciones.cargarEvidencia(r)} className={ENLACE_ACCION_CLS}>
        Cargar PDF
      </button>
    );
  }
  return (
    <span className="flex flex-col gap-1">
      <span className="flex min-w-0 flex-wrap items-center gap-x-2 text-xs">
        <span title={r.evidencia.nombre} className="max-w-[12rem] truncate font-semibold">
          {r.evidencia.nombre}
        </span>
        <span className="text-muted-foreground tabular-nums">{formatoTamano(r.evidencia.tamano)}</span>
      </span>
      <span className="flex flex-wrap gap-x-3">
        <button
          type="button"
          data-ver-evidencia={r.id}
          aria-label={`Ver evidencia de ${concepto}`}
          aria-busy={descarga === "descargando" || undefined}
          onClick={() => acciones.verEvidencia(r)}
          className={ENLACE_ACCION_CLS}
        >
          {descarga === "descargando" ? "Descargando…" : "Ver"}
        </button>
        <button type="button" aria-label={`Reemplazar evidencia de ${concepto}`} onClick={() => acciones.cargarEvidencia(r)} className={ENLACE_ACCION_CLS}>
          Reemplazar
        </button>
        <button type="button" aria-label={`Quitar evidencia de ${concepto}`} onClick={() => acciones.quitarEvidencia(r)} className={ENLACE_ACCION_CLS}>
          Quitar
        </button>
      </span>
      {descarga && descarga !== "descargando" && (
        <span role="alert" className="text-xs font-semibold text-[color:var(--di-danger)]">
          {descarga}
        </span>
      )}
    </span>
  );
}

/** Tabla editable de los conceptos (EN_GESTION). */
export function RenglonesEditables({ renglones, acciones, errores, descargas }: RenglonesEditablesProps) {
  // La identidad de cada fila sigue a la del renglón: TablaEditable (automático) rehace el borrador cuando cambia la fila,
  // y tras un guardado fallido eso borraría lo digitado. Un renglón que no cambió conserva su fila.
  const [cache] = useState(() => new WeakMap<RenglonSaneamiento, Fila>());
  const filas: Fila[] = renglones.map((r) => {
    let f = cache.get(r);
    if (!f) {
      f = { ...r, quitar: r.id };
      cache.set(r, f);
    }
    return f;
  });
  const marcar = (rid: string) => acciones.alEditar(rid, true);

  const columnas: ColumnaEditable<Fila>[] = [
    {
      campo: "fuente",
      titulo: "Concepto",
      mostrar: (f) => (
        <span className="flex flex-col gap-0.5">
          <span>{rotuloConcepto(f)}</span>
          {!f.automatico && <span className={`${AYUDA_CLS} font-normal`}>Agregado por FLIT</span>}
        </span>
      ),
    },
    { campo: "hallazgo", titulo: "Hallazgo", mostrar: (f) => (f.hallazgo ? ROTULO_HALLAZGO[f.hallazgo] : "—") },
    {
      campo: "resultado",
      titulo: "Resultado",
      editable: true,
      guardarAlCambiarMs: 600,
      editor: (valor, onCambio, etiqueta, fila, guardando) => (
        <div data-renglon-resultado={fila.id}>
          <SelectorSegmentado<ResultadoRenglon>
            etiqueta={etiqueta}
            opciones={OPCIONES_RESULTADO}
            valor={valor as ResultadoRenglon}
            bloqueado={guardando}
            className={SEGMENTADO_EN_LINEA_CLS}
            onCambio={(v) => {
              marcar(fila.id);
              onCambio(v as Fila["resultado"]);
            }}
          />
        </div>
      ),
    },
    {
      campo: "nota",
      titulo: "Nota",
      editable: true,
      editor: (valor, onCambio, etiqueta, fila, guardando) => {
        const texto = (valor as string | null) ?? "";
        const contador = `nota-${fila.id}-contador`;
        return (
          <span className={NOTA_CLS}>
            <textarea
              aria-label={etiqueta}
              aria-describedby={contador}
              rows={2}
              maxLength={LIMITES_TEXTO.nota.max}
              value={texto}
              readOnly={guardando}
              onChange={(e) => {
                marcar(fila.id);
                onCambio(e.target.value as Fila["nota"]);
              }}
              className={`${CAMPO_CLS.replace("h-11 ", "")} min-h-16 py-2 sm:text-sm`}
            />
            <Contador id={contador} largo={texto.length} />
          </span>
        );
      },
    },
    { campo: "evidencia", titulo: "Evidencia", mostrar: (f) => <CeldaEvidencia r={f} acciones={acciones} descarga={descargas[f.id]} /> },
    {
      campo: "quitar",
      titulo: "Quitar",
      mostrar: (f) => (
        <button type="button" aria-label={`Quitar concepto ${rotuloConcepto(f)}`} onClick={() => acciones.quitarRenglon(f)} className={ENLACE_ACCION_CLS}>
          Quitar concepto
        </button>
      ),
    },
  ];

  return (
    <TablaEditable<Fila>
      etiqueta="Conceptos a sanear"
      guardado="automatico"
      apilado="contenedor"
      filas={filas}
      columnas={columnas}
      clave={(f) => f.id}
      nombreFila={(f) => rotuloConcepto(f)}
      onGuardar={async (f) => {
        const renglon: Partial<Fila> = { ...f };
        delete renglon.quitar;
        await acciones.guardar(renglon as RenglonSaneamiento);
        acciones.alEditar(f.id, false);
      }}
      rotulosEstado={{ sin_cambios: "Sin cambios", editado: "Sin guardar", guardando: "Guardando…", error: "No se guardó" }}
      errorFila={(f) => errores[f.id] ?? null}
      vacio="No hay conceptos. Agrega uno con «Agregar otro concepto» o cierra como no procede."
    />
  );
}

/** Renglones de un saneamiento cerrado (SANEADO o NO_PROCEDE): solo lectura, con «Ver evidencia» (spec-ui §6.2). */
export function RenglonesLectura({
  renglones,
  verEvidencia,
  descargas,
}: {
  renglones: RenglonSaneamiento[];
  verEvidencia: (r: RenglonSaneamiento) => void;
  descargas: Readonly<Record<string, "descargando" | string>>;
}) {
  const columnas = ["Concepto", "Hallazgo", "Resultado", "Nota", "Evidencia"];
  if (renglones.length === 0) return <p className={AYUDA_CLS}>El saneamiento no tiene conceptos.</p>;
  return (
    <RegionDesplazable nombre="Conceptos del saneamiento" className={CONTENEDOR_TABLA_CLS}>
      <table aria-label="Conceptos a sanear" className={`w-full border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
        <thead className={TABLA_MOVIL_THEAD_CLS}>
          <tr>
            {columnas.map((c) => (
              <th key={c} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                {c}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className={TABLA_MOVIL_TBODY_CLS}>
          {renglones.map((r) => {
            const concepto = rotuloConcepto(r);
            const descarga = descargas[r.id];
            const celdas: ReactNode[] = [
              concepto,
              r.hallazgo ? ROTULO_HALLAZGO[r.hallazgo] : "—",
              <EtiquetaTono key="r" tono={TONO_RESULTADO[r.resultado] ?? "neutral"}>
                {ROTULO_RESULTADO[r.resultado] ?? ROTULO_RESULTADO.PENDIENTE}
              </EtiquetaTono>,
              r.nota?.trim() ? <span className="whitespace-pre-line">{r.nota}</span> : "—",
              r.evidencia ? (
                <span key="e" className="flex flex-col gap-1">
                  <span title={r.evidencia.nombre} className="max-w-[12rem] truncate text-xs">{`${r.evidencia.nombre} · ${formatoTamano(r.evidencia.tamano)}`}</span>
                  <button
                    type="button"
                    data-ver-evidencia={r.id}
                    aria-label={`Ver evidencia de ${concepto}`}
                    aria-busy={descarga === "descargando" || undefined}
                    onClick={() => verEvidencia(r)}
                    className={ENLACE_ACCION_CLS}
                  >
                    Ver evidencia
                  </button>
                  {descarga && descarga !== "descargando" && (
                    <span role="alert" className="text-xs font-semibold text-[color:var(--di-danger)]">
                      {descarga}
                    </span>
                  )}
                </span>
              ) : (
                "—"
              ),
            ];
            return (
              <tr key={r.id} aria-label={concepto} className={`${FILA_TABLA_CLS} ${FILA_TABLA_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS}`}>
                {celdas.map((celda, j) =>
                  j === 0 ? (
                    <th key={columnas[j]} scope="row" data-label={columnas[j]} className={`${CELDA_TABLA_CLS} text-left font-semibold ${TABLA_MOVIL_CELL_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS}`}>
                      {celda}
                    </th>
                  ) : (
                    <td key={columnas[j]} data-label={columnas[j]} className={`${CELDA_TABLA_CLS} break-words ${TABLA_MOVIL_CELL_CLS}`}>
                      {celda}
                    </td>
                  ),
                )}
              </tr>
            );
          })}
        </tbody>
      </table>
    </RegionDesplazable>
  );
}
