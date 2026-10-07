"use client";

import { Eye, Pencil } from "lucide-react";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { RowActionsMenu } from "@/components/atom/RowActionsMenu";
import { usePaginacion } from "@/components/atom/usePaginacion";
import type { MandatoFormatView } from "@/lib/api/admin-plataforma-mandatos";
import { formatFechaHora } from "@/lib/format/date";
import { resolveTipoNegocio, tipoNegocioLabel } from "@/lib/plataforma/mandato-templates";
import { TipoMandatoAyuda } from "./TipoMandatoAyuda";

export interface MandatosFormatosTableProps {
  formatos: readonly MandatoFormatView[];
  /** Código del formato cuyo documento se está abriendo (deshabilita las acciones). */
  previewing: string | null;
  onEdit: (code: string) => void;
  onPreview: (code: string) => void;
}

/**
 * HU #13175 — formatos de contrato de mandato en el modelo único de tabla (DataTable +
 * paginación numerada + «Filas por página»). Sin botones de crear ni eliminar: el catálogo lo
 * gestiona el equipo FLIT y el Super Admin solo edita los formatos existentes.
 */
export function MandatosFormatosTable({
  formatos,
  previewing,
  onEdit,
  onPreview,
}: MandatosFormatosTableProps) {
  const pg = usePaginacion();
  const pageRows = pg.paginar([...formatos]);

  const columns: DataTableColumn<MandatoFormatView>[] = [
    {
      key: "name",
      header: "Nombre",
      render: (row) => (
        <div className="flex flex-col gap-0.5">
          <span className="font-semibold text-[#162244] dark:text-white">{row.name}</span>
          <span className="font-mono text-[11px] text-[#59677D] dark:text-white/55">
            {row.code}
          </span>
        </div>
      ),
    },
    {
      key: "tipo",
      header: <TipoMandatoAyuda />,
      render: (row) => tipoNegocioLabel(resolveTipoNegocio(row.assignmentMode)),
    },
    {
      key: "version",
      header: "Versión vigente",
      render: (row) =>
        row.delegatesToOfficeTemplate
          ? "Por organismo"
          : row.currentVersion > 0
            ? `v${row.currentVersion}`
            : "De fábrica",
    },
    {
      key: "updatedAt",
      header: "Última edición",
      render: (row) => (row.updatedAt ? formatFechaHora(row.updatedAt) : "—"),
    },
    {
      key: "actions",
      header: "Acciones",
      align: "right",
      render: (row) => (
        <RowActionsMenu
          ariaLabel={`Acciones del formato ${row.name}`}
          subject={row.name}
          actions={[
            {
              icon: Pencil,
              label: `Editar formato ${row.name}`,
              tone: "primary",
              onClick: () => onEdit(row.code),
              disabled: previewing !== null,
            },
            ...(row.selectableAsRedaction
              ? [
                  {
                    icon: Eye,
                    label: `Ver documento del formato ${row.name}`,
                    onClick: () => onPreview(row.code),
                    disabled: previewing !== null,
                  },
                ]
              : []),
          ]}
        />
      ),
    },
  ];

  return (
    <DataTable
      columns={columns}
      rows={pageRows}
      getRowKey={(row) => row.code}
      ariaLabel="Formatos de contrato de mandato"
      emptyMessage="No hay formatos de contrato en el catálogo."
      minWidth={760}
      pagination={{
        page: Math.min(pg.page, Math.max(1, Math.ceil(formatos.length / pg.pageSize))),
        pageSize: pg.pageSize,
        totalCount: formatos.length,
        onPageChange: pg.setPage,
        onPageSizeChange: pg.setPageSize,
      }}
    />
  );
}
