"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import {
  createRejectionReason,
  fetchRejectionReasons,
  setRejectionReasonActive,
  updateRejectionReason,
} from "@/lib/api/ot-metrics";
import type { RejectionReason } from "@/lib/api/types-ot";
import { Pencil, PowerOff, RotateCcw } from "lucide-react";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { Pagination } from "@/components/atom/Pagination";
import { RowActions } from "@/components/atom/RowActions";
import { usePaginacion } from "@/components/atom/usePaginacion";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";

// Consola SuperAdmin del catálogo de causales de rechazo.
//
// El catálogo se sembró con las 18 causales que FLIT 1 usó durante dos años (unificando el
// duplicado «No tiene improntas»). Desde aquí se ajusta: agregar, editar y retirar.

const MODALIDADES = [
  { value: "matricula_inicial", label: "Matrícula inicial" },
  { value: "traspaso", label: "Traspaso" },
];

interface FormState {
  id: string | null;
  code: string;
  description: string;
  modalidad: string;
  sortOrder: string;
}

const emptyForm: FormState = {
  id: null,
  code: "",
  description: "",
  modalidad: "matricula_inicial",
  sortOrder: "",
};

export function RejectionReasonsConsole() {
  const [reasons, setReasons] = useState<RejectionReason[]>([]);
  const [form, setForm] = useState<FormState>(emptyForm);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const editorRef = useRef<HTMLFieldSetElement>(null);
  const [message, setMessage] = useState<string | null>(null);

  const load = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      // includeInactive: la consola necesita ver las retiradas para poder reactivarlas.
      setReasons(await fetchRejectionReasons({ includeInactive: true }));
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "No se pudo cargar el catálogo");
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: patrón del repo, skeleton inmediato antes del fetch
    void load();
  }, [load]);

  async function submit() {
    if (!form.code.trim() || !form.description.trim()) {
      setError("El código y la descripción son obligatorios.");
      return;
    }

    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      const body = {
        code: form.code.trim(),
        description: form.description.trim(),
        modalidad: form.modalidad,
        sortOrder: form.sortOrder.trim() === "" ? undefined : Number(form.sortOrder),
      };

      if (form.id) {
        await updateRejectionReason(form.id, body);
        setMessage("Causal actualizada.");
      } else {
        await createRejectionReason(body);
        setMessage("Causal creada.");
      }
      setForm(emptyForm);
      await load();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "No se pudo guardar la causal");
    } finally {
      setBusy(false);
    }
  }

  async function toggleActive(reason: RejectionReason) {
    setBusy(true);
    setError(null);
    try {
      await setRejectionReasonActive(reason.id, !reason.isActive);
      await load();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "No se pudo cambiar el estado de la causal");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-5" data-testid="rejection-reasons-console">
      <fieldset
        ref={editorRef}
        className="flex flex-wrap items-end gap-3 rounded-xl border border-[#DFE5ED] p-4 dark:border-white/10"
      >
        <legend className="px-1 text-xs font-semibold">
          {form.id ? "Editar causal" : "Nueva causal"}
        </legend>

        <label className="flex flex-col gap-1 text-xs font-semibold">
          Código
          <input
            value={form.code}
            onChange={(e) => setForm({ ...form, code: e.target.value })}
            placeholder="soat_no_vigente"
            className="w-48 rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
          />
        </label>

        <label className="flex flex-col gap-1 text-xs font-semibold">
          Descripción
          <input
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
            placeholder="SOAT no vigente"
            className="w-72 rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
          />
        </label>

        <label className="flex flex-col gap-1 text-xs font-semibold">
          Modalidad
          <select
            value={form.modalidad}
            onChange={(e) => setForm({ ...form, modalidad: e.target.value })}
            className="rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
          >
            {MODALIDADES.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label}
              </option>
            ))}
          </select>
        </label>

        <label className="flex flex-col gap-1 text-xs font-semibold">
          Orden
          <input
            value={form.sortOrder}
            onChange={(e) => setForm({ ...form, sortOrder: e.target.value })}
            inputMode="numeric"
            placeholder="auto"
            className="w-20 rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
          />
        </label>

        <button
          type="button"
          onClick={() => void submit()}
          disabled={busy}
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          {form.id ? "Guardar cambios" : "Crear causal"}
        </button>

        {form.id && (
          <button
            type="button"
            onClick={() => setForm(emptyForm)}
            disabled={busy}
            className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-60"
          >
            Cancelar
          </button>
        )}
      </fieldset>

      {error && (
        <p className="rounded-xl bg-red-50 px-4 py-3 text-xs text-red-700 dark:bg-red-500/10 dark:text-red-300">
          {error}
        </p>
      )}
      {message && (
        <p className="rounded-xl bg-emerald-50 px-4 py-3 text-xs text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-300">
          {message}
        </p>
      )}

      {busy && reasons.length === 0 && <CarLoaderModal label="Cargando causales de rechazo…" />}

      {MODALIDADES.map((m) => {
        const items = reasons.filter((r) => r.modalidad === m.value);
        return (
          <section key={m.value} className="flex flex-col gap-2">
            <h2 className="text-sm font-semibold">{m.label}</h2>
            {items.length === 0 ? (
              <p className="text-xs text-[#6B7280] dark:text-white/50">
                Sin causales para esta modalidad.
              </p>
            ) : (
              <ReasonsTable
                items={items}
                modalidadLabel={m.label}
                busy={busy}
                onEdit={(r) => {
                  setForm({
                    id: r.id,
                    code: r.code,
                    description: r.description,
                    modalidad: r.modalidad,
                    sortOrder: String(r.sortOrder),
                  });
                  // El formulario es uno solo y vive arriba del listado. Con 18
                  // causales en pantalla, editar una de las últimas filas cambiaba
                  // algo que quedaba fuera de la vista: el botón parecía no hacer nada.
                  editorRef.current?.scrollIntoView({ block: "center" });
                }}
                onToggle={(r) => void toggleActive(r)}
              />
            )}
          </section>
        );
      })}

      {/* No hay borrado a propósito: una causal retirada debe seguir resolviendo el nombre de los
          rechazos históricos que la usaron. */}
      <p className="text-[11px] text-[#6B7280] dark:text-white/50">
        Las causales no se borran: se retiran. Así los rechazos históricos conservan el nombre de la
        causal con la que se decidieron.
      </p>
    </div>
  );
}

// Bug #13055 — tabla homologada con el modelo de trámites (table-styles, RowActions y Pagination con
// filas por página, en cliente). Una por modalidad, cada una con su propia página.
function ReasonsTable({
  items,
  modalidadLabel,
  busy,
  onEdit,
  onToggle,
}: {
  items: RejectionReason[];
  modalidadLabel: string;
  busy: boolean;
  onEdit: (reason: RejectionReason) => void;
  onToggle: (reason: RejectionReason) => void;
}) {
  const pg = usePaginacion();
  const head = { background: TABLA_HEADER_BG, color: TABLA_HEADER_FG } as const;
  const cell = { borderColor: "#DFE5ED" } as const;

  return (
    <div className="flex flex-col">
      <div className="overflow-x-auto">
        <table
          className="min-w-[36rem] text-xs"
          style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
        >
          <caption className="sr-only">Causales de rechazo — {modalidadLabel}</caption>
          <thead>
            <tr>
              <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`} style={head}>
                Descripción
              </th>
              <th scope="col" className={TABLA_HEADER_CELL_CLS} style={head}>
                Código
              </th>
              <th scope="col" className={TABLA_HEADER_CELL_CLS} style={head}>
                Orden
              </th>
              <th scope="col" className={TABLA_HEADER_CELL_CLS} style={head}>
                Estado
              </th>
              <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`} style={head}>
                Acciones
              </th>
            </tr>
          </thead>
          <tbody>
            {pg.paginar(items).map((r) => (
              <tr
                key={r.id}
                className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS} ${
                  r.isActive ? "" : "opacity-60"
                }`}
              >
                <td className="rounded-l-xl border-y border-l px-4 py-3" style={cell}>
                  {r.description}
                </td>
                <td className="border-y px-4 py-3 font-mono text-[11px]" style={cell}>
                  {r.code}
                </td>
                <td className="border-y px-4 py-3 tabular-nums" style={cell}>
                  {r.sortOrder}
                </td>
                <td className="border-y px-4 py-3" style={cell}>
                  {r.isActive ? "Activa" : "Retirada"}
                </td>
                <td className="rounded-r-xl border-y border-r px-4 py-3 text-right" style={cell}>
                  <RowActions
                    actions={[
                      {
                        icon: Pencil,
                        label: `Editar causal ${r.description}`,
                        onClick: () => onEdit(r),
                        tone: "primary",
                      },
                      {
                        icon: r.isActive ? PowerOff : RotateCcw,
                        label: `${r.isActive ? "Retirar" : "Reactivar"} causal ${r.description}`,
                        onClick: () => onToggle(r),
                        disabled: busy,
                        tone: r.isActive ? "danger" : "primary",
                      },
                    ]}
                  />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <Pagination
        page={pg.page}
        pageSize={pg.pageSize}
        totalCount={items.length}
        onPageChange={pg.setPage}
        onPageSizeChange={pg.setPageSize}
        ariaLabel={`Paginación de causales — ${modalidadLabel}`}
        noun="causales"
      />
    </div>
  );
}
