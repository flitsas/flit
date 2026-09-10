"use client";

import { Pencil, Trash2 } from "lucide-react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { bannerEstadoTone } from "@/components/atom/statusTones";
import { RowActions } from "@/components/atom/RowActions";
import { Pagination } from "@/components/atom/Pagination";
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
 * Tabla administrable de banners promocionales (HU #12241 AC1). Columnas: Nombre, Imagen
 * (preview cargado desde el endpoint público de imagen — nunca desde el campo crudo `imageUrl`
 * del backend, ver `bannerImageUrl`), Tiene enlace, Estado (color por `bannerEstadoTone`), Fecha
 * inicio/fin ("Sin fecha programada" si no aplica) y Acciones (Editar/Eliminar).
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
  return (
    <div className="flex flex-1 flex-col">
      <div className="overflow-x-auto">
        <table className="w-full min-w-[820px] border-separate border-spacing-y-2 text-xs">
          <thead>
            <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
              <th className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                Nombre
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                Imagen
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                Tiene enlace
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                Estado
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                Fecha inicio / fin
              </th>
              <th className="rounded-r-xl px-4 py-2.5 text-right" style={{ background: "#DFE5ED" }} scope="col">
                Acciones
              </th>
            </tr>
          </thead>
          <tbody>
            {items.map((b) => (
              <tr key={b.id} className="bg-white dark:bg-[#0B0F14]">
                <td className="rounded-l-xl border-y border-l px-4 py-3 font-semibold">{b.name}</td>
                <td className="border-y px-4 py-3">
                  {/* eslint-disable-next-line @next/next/no-img-element -- preview binaria vía endpoint público streaming, no un asset estático */}
                  <img
                    src={bannerImageUrl(b.id)}
                    alt={`Vista previa del banner ${b.name}`}
                    className="h-10 w-20 rounded-lg border object-cover"
                    style={{ borderColor: "#DFE5ED" }}
                  />
                </td>
                <td className="border-y px-4 py-3">
                  {b.linkUrl ? (
                    <StatusBadge label="SÍ" tone="info" ariaLabel={`${b.name} tiene enlace`} />
                  ) : (
                    <StatusBadge label="NO" tone="neutral" ariaLabel={`${b.name} no tiene enlace`} />
                  )}
                </td>
                <td className="border-y px-4 py-3">
                  <StatusBadge label={ESTADO_LABEL[b.estado]} tone={bannerEstadoTone(b.estado)} />
                </td>
                <td className="border-y px-4 py-3 opacity-80">
                  <div className="flex flex-col gap-0.5">
                    <span>Inicio: {formatDate(b.validFrom)}</span>
                    <span>Fin: {formatDate(b.validUntil)}</span>
                  </div>
                </td>
                <td className="rounded-r-xl border-y border-r px-4 py-3 text-right">
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
    </div>
  );
}

function formatDate(iso: string | null): string {
  if (!iso) return "Sin fecha programada";
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return "Sin fecha programada";
  return parsed.toLocaleDateString("es-CO", { year: "numeric", month: "2-digit", day: "2-digit", timeZone: "UTC" });
}
