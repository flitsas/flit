"use client";

import { useState } from "react";
import { ExternalLink, Pencil, Trash2 } from "lucide-react";
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
} from "@/components/atom/table-styles";
import { bannerImageUrl, type Banner, type BannerEstado } from "@/lib/api/admin-banners";

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
  onEdit,
  onDelete,
}: BannerListTableProps) {
  // Visualizador de imagen completa (clic en la miniatura de la tabla): el banner original ya
  // cumple la proporción recomendada, así que un <img> normal dentro del Modal basta — no hace
  // falta blob/descarga autenticada, el endpoint público ya sirve el binario directo.
  const [preview, setPreview] = useState<Banner | null>(null);

  return (
    <div className="flex flex-1 flex-col">
      <div className="overflow-x-auto">
        <table
          aria-label="Banners promocionales"
          style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
        >
          <thead>
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
          <tbody>
            {items.map((b) => (
              <tr key={b.id} className={`bg-white text-xs dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
                <td className="rounded-l-xl border-y border-l px-4 py-3 font-semibold" style={{ borderColor: "#DFE5ED" }}>
                  {b.name}
                </td>
                <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                  {/* AC1 mejora: la miniatura no recorta (object-contain) y, al hacer clic, abre
                      la imagen completa en un visualizador — la miniatura sigue siendo chica para
                      no romper el alto de la fila, pero nada del banner queda oculto. */}
                  <button
                    type="button"
                    onClick={() => setPreview(b)}
                    aria-label={`Ver la imagen completa del banner ${b.name}`}
                    title="Ver imagen completa"
                    className="flex h-14 w-32 items-center justify-center overflow-hidden rounded-lg border bg-[#F4F7FC] transition hover:opacity-80 dark:bg-white/5"
                    style={{ borderColor: "#DFE5ED" }}
                  >
                    {/* eslint-disable-next-line @next/next/no-img-element -- preview binaria vía endpoint público streaming, no un asset estático */}
                    <img
                      src={bannerImageUrl(b.id)}
                      alt={`Vista previa del banner ${b.name}`}
                      className="h-full w-full object-contain"
                    />
                  </button>
                </td>
                <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                  {b.linkUrl ? (
                    <span className="inline-flex items-center gap-1">
                      <StatusBadge label="SÍ" tone="info" ariaLabel={`${b.name} tiene enlace`} />
                      <a
                        href={b.linkUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        aria-label={`Abrir el enlace del banner ${b.name}`}
                        title={b.linkUrl}
                        className="inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full transition hover:bg-[#557EFF]/10"
                        style={{ color: "#557EFF" }}
                      >
                        <ExternalLink className="h-3.5 w-3.5" />
                      </a>
                    </span>
                  ) : (
                    <StatusBadge label="NO" tone="neutral" ariaLabel={`${b.name} no tiene enlace`} />
                  )}
                </td>
                <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                  <StatusBadge label={ESTADO_LABEL[b.estado]} tone={bannerEstadoTone(b.estado)} />
                </td>
                <td className="border-y px-4 py-3 opacity-80" style={{ borderColor: "#DFE5ED" }}>
                  {formatDate(b.validFrom)}
                </td>
                <td className="border-y px-4 py-3 opacity-80" style={{ borderColor: "#DFE5ED" }}>
                  {formatDate(b.validUntil)}
                </td>
                <td className="rounded-r-xl border-y border-r px-4 py-3 text-right" style={{ borderColor: "#DFE5ED" }}>
                  <RowActions
                    actions={[
                      {
                        icon: Pencil,
                        label: `Editar ${b.name}`,
                        onClick: () => onEdit(b),
                        tone: "primary",
                      },
                      {
                        icon: Trash2,
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

      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={onPageChange} className="mt-auto" />

      <Modal
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

function formatDate(iso: string | null): string {
  if (!iso) return "Sin fecha programada";
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return "Sin fecha programada";
  return parsed.toLocaleDateString("es-CO", { year: "numeric", month: "2-digit", day: "2-digit", timeZone: "UTC" });
}
