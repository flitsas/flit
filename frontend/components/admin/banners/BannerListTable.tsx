"use client";

import { useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { iconoComo } from "@flit/ui/iconoComo";
import { Modal } from "@/components/atom/Modal";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { bannerEstadoTone } from "@/components/atom/statusTones";
import { RowActions } from "@/components/atom/RowActions";
import { Pagination } from "@/components/atom/Pagination";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
  TABLA_TARJETA_CLS,
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@/components/atom/table-styles";
import { bannerImageUrl, type Banner, type BannerEstado } from "@/lib/api/admin-banners";
import { formatFechaHora } from "@/lib/format/date";

const ESTADO_LABEL: Record<BannerEstado, string> = {
  programado: "Programado",
  activo: "Activo",
  inactivo: "Inactivo",
  expirado: "Expirado",
};

export interface BannerListTableProps {
  items: Banner[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  /** Filas por página (Bug #13055). */
  onPageSizeChange?: (pageSize: number) => void;
  onEdit: (banner: Banner) => void;
  onDelete: (banner: Banner) => void;
}

/**
 * Tabla administrable de banners promocionales (HU #12241 AC1). Mismo lenguaje visual que
 * `TramitesTable` (`@/components/atom/table-styles`): SIN tarjeta blanca envolvente (la pone la
 * página si hace falta, aquí no) — filas como tarjetas propias (`border-spacing` + esquinas
 * redondeadas + sombra al pasar el puntero) sobre el fondo de la app, cabecera gris fija al hacer
 * scroll. Columnas: Nombre, Imagen (preview cargado desde el endpoint público de imagen — nunca
 * desde el campo crudo `imageUrl` del backend, ver `bannerImageUrl`), Tiene enlace, Estado (color
 * por `bannerEstadoTone`), Fecha inicio, Fecha fin (columnas separadas — no una sola celda con las
 * dos fechas apiladas) y Acciones (Editar/Eliminar).
 */
export function BannerListTable({
  items,
  totalCount,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
  onEdit,
  onDelete,
}: BannerListTableProps) {
  // Visualizador de imagen completa (clic en la miniatura de la tabla): el banner original ya
  // cumple la proporción recomendada, así que un <img> normal dentro del Modal basta — no hace
  // falta blob/descarga autenticada, el endpoint público ya sirve el binario directo.
  const [preview, setPreview] = useState<Banner | null>(null);

  return (
    <div className={`flex flex-1 flex-col ${TABLA_TARJETA_CLS}`}>
      <div className="overflow-x-auto">
        <table className={TABLA_MOVIL_TABLE_CLS}
          aria-label="Banners promocionales"
          style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
        >
          <thead className={TABLA_MOVIL_THEAD_CLS}>
            <tr>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Nombre
              </th>
              <th
                scope="col"
                className={TABLA_HEADER_CELL_CLS}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Imagen
              </th>
              <th
                scope="col"
                className={TABLA_HEADER_CELL_CLS}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Tiene enlace
              </th>
              <th
                scope="col"
                className={TABLA_HEADER_CELL_CLS}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Estado
              </th>
              <th
                scope="col"
                className={TABLA_HEADER_CELL_CLS}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Fecha inicio
              </th>
              <th
                scope="col"
                className={TABLA_HEADER_CELL_CLS}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Fecha fin
              </th>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Acciones
              </th>
            </tr>
          </thead>
          <tbody className={TABLA_MOVIL_TBODY_CLS}>
            {items.map((b) => (
              <tr key={b.id} className={`bg-card text-xs ${TABLA_ROW_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS}`}>
                <td className={`rounded-l-xl border-y border-l px-4 py-3 font-semibold ${TABLA_MOVIL_CELL_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS}`} data-label="Nombre" style={{ borderColor: "var(--border)" }}>
                  {b.name}
                </td>
                <td className={`border-y px-4 py-3 ${TABLA_MOVIL_CELL_CLS}`} data-label="Imagen" style={{ borderColor: "var(--border)" }}>
                  {/* AC1 mejora: la miniatura no recorta (object-contain) y, al hacer clic, abre
                      la imagen completa en un visualizador — la miniatura sigue siendo chica para
                      no romper el alto de la fila, pero nada del banner queda oculto. */}
                  <button
                    type="button"
                    onClick={() => setPreview(b)}
                    aria-label={`Ver la imagen completa del banner ${b.name}`}
                    title="Ver imagen completa"
                    className="flex h-14 w-32 items-center justify-center overflow-hidden rounded-lg border bg-secondary transition hover:opacity-80"
                    style={{ borderColor: "var(--border)" }}
                  >
                    {/* eslint-disable-next-line @next/next/no-img-element -- preview binaria vía endpoint público streaming, no un asset estático */}
                    <img
                      src={bannerImageUrl(b.id)}
                      alt={`Vista previa del banner ${b.name}`}
                      className="h-full w-full object-contain"
                    />
                  </button>
                </td>
                <td className={`border-y px-4 py-3 ${TABLA_MOVIL_CELL_CLS}`} data-label="Tiene enlace" style={{ borderColor: "var(--border)" }}>
                  {b.linkUrl ? (
                    <span className="inline-flex items-center gap-1">
                      <StatusBadge label="SÍ" tone="info" ariaLabel={`${b.name} tiene enlace`} />
                      <a
                        href={b.linkUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        aria-label={`Abrir el enlace del banner ${b.name}`}
                        title={b.linkUrl}
                        className="inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full transition hover:bg-flit-brand/10"
                        style={{ color: "var(--color-flit-brand)" }}
                      >
                        <Icono nombre="externo" className="h-3.5 w-3.5" />
                      </a>
                    </span>
                  ) : (
                    <StatusBadge label="NO" tone="neutral" ariaLabel={`${b.name} no tiene enlace`} />
                  )}
                </td>
                <td className={`border-y px-4 py-3 ${TABLA_MOVIL_CELL_CLS}`} data-label="Estado" style={{ borderColor: "var(--border)" }}>
                  <StatusBadge label={ESTADO_LABEL[b.estado]} tone={bannerEstadoTone(b.estado)} />
                </td>
                <td className={`border-y px-4 py-3 opacity-80 ${TABLA_MOVIL_CELL_CLS}`} data-label="Fecha inicio" style={{ borderColor: "var(--border)" }}>
                  {formatFechaHora(b.validFrom, "Sin fecha programada")}
                </td>
                <td className={`border-y px-4 py-3 opacity-80 ${TABLA_MOVIL_CELL_CLS}`} data-label="Fecha fin" style={{ borderColor: "var(--border)" }}>
                  {formatFechaHora(b.validUntil, "Sin fecha programada")}
                </td>
                <td className={`rounded-r-xl border-y border-r px-4 py-3 text-right ${TABLA_MOVIL_CELL_CLS}`} data-label="Acciones" style={{ borderColor: "var(--border)" }}>
                  <RowActions
                    actions={[
                      {
                        icon: iconoComo("lapiz"),
                        label: `Editar ${b.name}`,
                        onClick: () => onEdit(b),
                        tone: "primary",
                      },
                      {
                        icon: iconoComo("papelera"),
                        label: `Eliminar ${b.name}`,
                        onClick: () => onDelete(b),
                        tone: "danger",
                      },
                    ]}
                  />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={onPageChange} onPageSizeChange={onPageSizeChange} className="mt-auto" />

      <Modal
        icono="ojo"
        open={preview !== null}
        onClose={() => setPreview(null)}
        title={preview?.name ?? "Banner"}
        size="xl"
      >
        {preview && (
          // eslint-disable-next-line @next/next/no-img-element -- misma imagen binaria del endpoint público, sin recortar (object-contain)
          <img
            src={bannerImageUrl(preview.id)}
            alt={`Imagen completa del banner ${preview.name}`}
            className="max-h-[75vh] w-full rounded-xl object-contain"
          />
        )}
      </Modal>
    </div>
  );
}
