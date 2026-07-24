"use client";

import { useEffect, useState } from "react";
import {
  assignPlateRange,
  editPlateRange,
  listEligibleCompanies,
  listPlateRanges,
  type EligibleCompany,
  type PlateRangeSummary,
} from "@/lib/api/admin-plate-ranges";

// HU #10652 (Feature #10587) — consola OT para asignar/editar rangos de placas a una compañía.
export interface PlateRangesConsoleProps {
  transitOfficeId: string;
}

const emptyForm = { prefix: "", rangeFrom: "", rangeTo: "" };

export function PlateRangesConsole({ transitOfficeId }: PlateRangesConsoleProps) {
  const [companyTenantId, setCompanyTenantId] = useState("");
  // HU #10797 — compañías elegibles del OT (preasignación activa + grant): alimentan el selector.
  const [companies, setCompanies] = useState<EligibleCompany[]>([]);
  const [companiesLoaded, setCompaniesLoaded] = useState(false);
  const [ranges, setRanges] = useState<PlateRangeSummary[]>([]);
  const [form, setForm] = useState(emptyForm);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  // Snapshot de "ahora" para la ventana de edición de 60 min (se fija en cada carga, no en render).
  const [now, setNow] = useState(0);

  const scope = { transitOfficeId };

  // Carga las compañías elegibles al montar (una sola vez por OT).
  useEffect(() => {
    const controller = new AbortController();
    listEligibleCompanies({ transitOfficeId }, controller.signal)
      .then((data) => {
        setCompanies(data);
        setCompaniesLoaded(true);
      })
      .catch(() => setCompaniesLoaded(true));
    return () => controller.abort();
  }, [transitOfficeId]);

  async function load() {
    if (!companyTenantId.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const data = await listPlateRanges(companyTenantId.trim(), scope);
      // Snapshot de "ahora" al cargar (no en render, por pureza): la ventana de edición de 60 min
      // se evalúa contra este valor; se refresca en cada carga (montaje y tras cada acción).
      setNow(Date.now());
      setRanges(data);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Error al cargar rangos");
    } finally {
      setBusy(false);
    }
  }

  async function submit() {
    const rangeFrom = Number(form.rangeFrom);
    const rangeTo = Number(form.rangeTo);
    if (!companyTenantId.trim() || !form.prefix || Number.isNaN(rangeFrom) || Number.isNaN(rangeTo)) {
      setError("Completa compañía, prefijo y el rango numérico.");
      return;
    }
    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      if (editingId) {
        await editPlateRange(editingId, { prefix: form.prefix.toUpperCase(), rangeFrom, rangeTo });
        setMessage("Rango editado.");
      } else {
        const r = await assignPlateRange({
          companyTenantId: companyTenantId.trim(),
          prefix: form.prefix.toUpperCase(),
          rangeFrom,
          rangeTo,
          transitOfficeId,
        });
        setMessage(`Rango asignado (${r.platesCreated} placas).`);
      }
      setForm(emptyForm);
      setEditingId(null);
      await load();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "No se pudo guardar el rango");
    } finally {
      setBusy(false);
    }
  }

  const isEditable = (r: PlateRangeSummary) => new Date(r.editableUntil).getTime() > now;

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-end gap-3">
        <label className="flex flex-col gap-1 text-xs font-semibold">
          Compañía
          <select
            value={companyTenantId}
            onChange={(e) => setCompanyTenantId(e.target.value)}
            disabled={companies.length === 0}
            aria-label="Compañía con preasignación de placa activa"
            className="w-full sm:w-72 rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF] disabled:opacity-60"
          >
            <option value="">
              {companies.length === 0
                ? companiesLoaded
                  ? "No hay compañías con preasignación activa"
                  : "Cargando compañías…"
                : "Selecciona una compañía"}
            </option>
            {companies.map((c) => (
              <option key={c.tenantId} value={c.tenantId}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
        <button
          type="button"
          onClick={load}
          disabled={busy}
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          Cargar rangos
        </button>
      </div>

      <fieldset className="flex flex-wrap items-end gap-3 rounded-xl border border-[#DFE5ED] p-4 dark:border-white/10">
        <legend className="px-1 text-xs font-semibold">{editingId ? "Editar rango" : "Asignar rango"}</legend>
        <label className="flex flex-col gap-1 text-xs font-semibold">
          Prefijo
          <input
            value={form.prefix}
            onChange={(e) => setForm((f) => ({ ...f, prefix: e.target.value }))}
            maxLength={3}
            placeholder="ABC"
            className="w-24 rounded-xl border bg-transparent px-3 py-2 text-xs uppercase outline-none focus:border-[#557EFF]"
          />
        </label>
        <label className="flex flex-col gap-1 text-xs font-semibold">
          Desde
          <input
            value={form.rangeFrom}
            onChange={(e) => setForm((f) => ({ ...f, rangeFrom: e.target.value }))}
            inputMode="numeric"
            placeholder="100"
            className="w-24 rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
          />
        </label>
        <label className="flex flex-col gap-1 text-xs font-semibold">
          Hasta
          <input
            value={form.rangeTo}
            onChange={(e) => setForm((f) => ({ ...f, rangeTo: e.target.value }))}
            inputMode="numeric"
            placeholder="200"
            className="w-24 rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
          />
        </label>
        <button
          type="button"
          onClick={submit}
          disabled={busy}
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
          style={{ background: "#557EFF" }}
        >
          {editingId ? "Guardar cambios" : "Asignar"}
        </button>
        {editingId && (
          <button
            type="button"
            onClick={() => {
              setEditingId(null);
              setForm(emptyForm);
            }}
            className="rounded-xl border px-4 py-2 text-xs font-semibold"
          >
            Cancelar
          </button>
        )}
      </fieldset>

      {message && <p className="text-xs font-medium" style={{ color: "#15803d" }}>{message}</p>}
      {error && <p className="text-xs font-medium" role="alert" style={{ color: "#FF4E00" }}>{error}</p>}

      {ranges.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="border-b opacity-70">
                <th className="px-2 py-2">Rango</th>
                <th className="px-2 py-2">Disponibles</th>
                <th className="px-2 py-2">Total</th>
                <th className="px-2 py-2">Editable hasta</th>
                <th className="px-2 py-2"></th>
              </tr>
            </thead>
            <tbody>
              {ranges.map((r) => (
                <tr key={r.id} className="border-b border-black/5 dark:border-white/5">
                  <td className="px-2 py-2 font-mono font-semibold">
                    {r.prefix}
                    {String(r.rangeFrom).padStart(3, "0")}–{r.prefix}
                    {String(r.rangeTo).padStart(3, "0")}
                  </td>
                  <td className="px-2 py-2">{r.availablePlates}</td>
                  <td className="px-2 py-2">{r.totalPlates}</td>
                  <td className="px-2 py-2 opacity-70">{new Date(r.editableUntil).toLocaleString("es-CO")}</td>
                  <td className="px-2 py-2">
                    <button
                      type="button"
                      disabled={!isEditable(r)}
                      onClick={() => {
                        setEditingId(r.id);
                        setForm({ prefix: r.prefix, rangeFrom: String(r.rangeFrom), rangeTo: String(r.rangeTo) });
                      }}
                      className="rounded-lg border px-3 py-1 text-[11px] font-semibold disabled:opacity-40"
                      title={isEditable(r) ? "Editar (ventana de 60 min)" : "Ventana de edición vencida"}
                    >
                      Editar
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
