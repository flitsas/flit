"use client";

import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import {
  fetchFurPreview,
  type FurPersonKind,
  type FurPrendaKind,
  type FurVehicleKind,
} from "@/lib/api/admin-plataforma-fur";
import { superadminClient } from "@/lib/api/superadmin-client";
import { openPdfBlobInNewTab } from "@/lib/documents/open-document-tab";
import { buildFurGuide } from "@/lib/fur/fur-guide";
import type { ProcedureFamily, ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";

const VEHICLES: { value: FurVehicleKind; label: string }[] = [
  { value: "carro", label: "Carro" },
  { value: "moto", label: "Moto" },
  { value: "remolque", label: "Remolque" },
  { value: "maquinaria", label: "Maquinaria" },
];

const PERSONS: { value: FurPersonKind; label: string }[] = [
  { value: "natural", label: "Persona natural" },
  { value: "juridica", label: "Persona jurídica" },
];

const PRENDA: { value: FurPrendaKind; label: string }[] = [
  { value: "ninguna", label: "No aplica" },
  { value: "inscripcion", label: "Inscripción de prenda" },
  { value: "levantamiento", label: "Levantamiento de prenda" },
  { value: "ambas", label: "Ambas (inscripción y levantamiento)" },
];

const FAMILY_LABEL: Record<string, string> = {
  MATRICULAS: "Matrículas",
  TRASPASO: "Traspaso",
  OTROS: "Otros",
};

function sellerApplies(type: ProcedureTypeSummary | undefined): boolean {
  if (!type) return false;
  return type.family === "TRASPASO" || type.code.toUpperCase().includes("TRASPASO");
}

function inferFromType(type: ProcedureTypeSummary | undefined) {
  const code = (type?.code ?? "").toUpperCase();
  const prenda: FurPrendaKind =
    code === "LEVANTAR_INSCRIBIR_PRENDA"
      ? "ambas"
      : code.includes("LEVANTAMIENTO_PRENDA")
        ? "levantamiento"
        : code.includes("PRENDA_INSCRIPCION") || code.includes("INSCRIBIR_PRENDA")
          ? "inscripcion"
          : "ninguna";
  return {
    color: code === "CAMBIO_COLOR",
    combustible: code === "CONVERSION_COMBUSTIBLE",
    carroceria: code === "CAMBIO_CARROCERIA",
    blindaje: code.includes("BLINDAJE"),
    prenda,
    lockColor: code === "CAMBIO_COLOR",
    lockCombustible: code === "CONVERSION_COMBUSTIBLE",
    lockCarroceria: code === "CAMBIO_CARROCERIA",
    lockBlindaje: code.includes("BLINDAJE"),
    lockPrenda:
      code === "PRENDA_INSCRIPCION" ||
      code === "LEVANTAMIENTO_PRENDA" ||
      code === "LEVANTAR_INSCRIBIR_PRENDA",
  };
}

function SelectField({
  id,
  label,
  value,
  onChange,
  disabled,
  children,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  children: ReactNode;
}) {
  return (
    <label className="flex flex-col gap-1.5 text-sm" htmlFor={id}>
      <span className="font-semibold text-[#162244] dark:text-white">{label}</span>
      <select
        id={id}
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
        className="rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:cursor-not-allowed disabled:opacity-60 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
      >
        {children}
      </select>
    </label>
  );
}

function CheckboxField({
  id,
  label,
  checked,
  onChange,
  disabled,
}: {
  id: string;
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
}) {
  return (
    <label className="flex items-center gap-2 text-sm text-[#162244] dark:text-white" htmlFor={id}>
      <input
        id={id}
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
        className="h-4 w-4 rounded border-[#DFE5ED] disabled:cursor-not-allowed disabled:opacity-60"
      />
      {label}
    </label>
  );
}

function FurResultGuide({
  code,
  family,
  prenda,
  color,
  carroceria,
  combustible,
  blindaje,
}: {
  code: string;
  family: string;
  prenda: FurPrendaKind;
  color: boolean;
  carroceria: boolean;
  combustible: boolean;
  blindaje: boolean;
}) {
  const guide = buildFurGuide({ code, family, prenda, color, carroceria, combustible, blindaje });
  return (
    <section
      className="md:col-span-2 rounded-2xl border border-[#DFE5ED] bg-[#F7F9FC] p-4 dark:border-white/10 dark:bg-white/5"
      aria-labelledby="fur-guide-title"
      data-testid="fur-result-guide"
    >
      <h2 id="fur-guide-title" className="text-sm font-semibold text-[#162244] dark:text-white">
        Qué debería llevar este FUR
      </h2>
      <p className="mt-1 text-xs text-[#5B6475] dark:text-white/60">
        Guía previa al PDF. Se actualiza al cambiar tipo, prenda o transformaciones.
      </p>
      <div className="mt-3 grid gap-4 md:grid-cols-2">
        <div>
          <h3 className="text-xs font-semibold uppercase tracking-wide text-[#59677D] dark:text-white/55">
            Numeral 3 — trámite solicitado
          </h3>
          {guide.casillas.length === 0 ? (
            <p className="mt-2 text-sm text-[#5B6475] dark:text-white/70">Sin casillas en el numeral 3.</p>
          ) : (
            <ul className="mt-2 flex flex-wrap gap-2">
              {guide.casillas.map((c) => (
                <li
                  key={c.n}
                  className="inline-flex items-center gap-1.5 rounded-full bg-white px-2.5 py-1 text-sm text-[#162244] ring-1 ring-[#DFE5ED] dark:bg-[#0B0F14] dark:text-white dark:ring-white/15"
                >
                  <span className="font-semibold tabular-nums">{c.n}</span>
                  <span>{c.label}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
        <div>
          <h3 className="text-xs font-semibold uppercase tracking-wide text-[#59677D] dark:text-white/55">
            Párrafo 23 — observaciones
          </h3>
          {guide.observaciones.length === 0 ? (
            <p className="mt-2 text-sm text-[#5B6475] dark:text-white/70">
              Sin texto automático. Solo lo que el gestor escriba, si aplica.
            </p>
          ) : (
            <ol className="mt-2 list-decimal space-y-1.5 pl-4 text-sm text-[#162244] dark:text-white">
              {guide.observaciones.map((o) => (
                <li key={o} className="leading-snug">
                  {o}
                </li>
              ))}
            </ol>
          )}
        </div>
      </div>
      {guide.notas.length > 0 ? (
        <ul className="mt-3 space-y-1 text-xs text-[#5B6475] dark:text-white/60">
          {guide.notas.map((n) => (
            <li key={n}>{n}</li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}
export function FurSimulatorPanel() {
  const { show: showToast } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [types, setTypes] = useState<ProcedureTypeSummary[]>([]);
  const [family, setFamily] = useState<string>("");
  const [typeId, setTypeId] = useState("");
  const [sellerKind, setSellerKind] = useState<FurPersonKind>("natural");
  const [buyerKind, setBuyerKind] = useState<FurPersonKind>("natural");
  const [vehicleKind, setVehicleKind] = useState<FurVehicleKind | "">("");
  const [cambioColor, setCambioColor] = useState(false);
  const [cambioCombustible, setCambioCombustible] = useState(false);
  const [cambioCarroceria, setCambioCarroceria] = useState(false);
  const [blindaje, setBlindaje] = useState(false);
  const [prenda, setPrenda] = useState<FurPrendaKind>("ninguna");
  const [previewLoading, setPreviewLoading] = useState(false);

  const load = useCallback(async () => {
    setStatus("loading");
    try {
      const items = await superadminClient.listProcedureTypes();
      const active = items.filter((t) => t.isActive);
      setTypes(active);
      setStatus(active.length === 0 ? "empty" : "ready");
    } catch {
      setStatus("error");
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const families = useMemo(() => {
    const set = new Set<ProcedureFamily>();
    for (const t of types) set.add(t.family);
    return [...set].sort();
  }, [types]);

  const typesInFamily = useMemo(
    () => types.filter((t) => t.family === family),
    [types, family],
  );

  const selectedType = types.find((t) => t.id === typeId);
  const inferredLocks = inferFromType(selectedType);
  const canChooseSeller = sellerApplies(selectedType);
  const canSimulate = Boolean(family && typeId && vehicleKind);

  const applyTypeDefaults = (id: string) => {
    setTypeId(id);
    const type = types.find((t) => t.id === id);
    const inferred = inferFromType(type);
    setCambioColor(inferred.color);
    setCambioCombustible(inferred.combustible);
    setCambioCarroceria(inferred.carroceria);
    setBlindaje(inferred.blindaje);
    setPrenda(inferred.prenda);
  };

  const simulate = async () => {
    if (!canSimulate || !typeId || !vehicleKind) return;
    setPreviewLoading(true);
    try {
      await openPdfBlobInNewTab(
        () =>
          fetchFurPreview({
            procedureTypeId: typeId,
            sellerPersonKind: canChooseSeller ? sellerKind : "natural",
            buyerPersonKind: buyerKind,
            vehicleKind,
            cambioColor,
            cambioCombustible,
            cambioCarroceria,
            blindaje,
            prenda,
          }),
        { maximize: true },
      );
    } catch {
      showToast(
        "No se pudo generar el FUR de simulación. Verifica la sesión SuperAdmin e inténtalo de nuevo.",
        "error",
      );
    } finally {
      setPreviewLoading(false);
    }
  };

  return (
    <div data-testid="fur-simulator-panel" className="flex flex-col gap-6">
      <UiStateBoundary
        status={status}
        onRetry={load}
        errorMessage="No se pudo cargar el catálogo de tipos de trámite."
        emptyMessage="No hay tipos de trámite activos en tramites.procedure_types."
        skeletonRows={4}
      >
        <form
          className="grid gap-4 md:grid-cols-2"
          onSubmit={(e) => {
            e.preventDefault();
            void simulate();
          }}
        >
          <SelectField
            id="fur-family"
            label="Tipo de trámite padre (familia)"
            value={family}
            onChange={(v) => {
              setFamily(v);
              setTypeId("");
            }}
          >
            <option value="">Selecciona una familia</option>
            {families.map((f) => (
              <option key={f} value={f}>
                {FAMILY_LABEL[f] ?? f}
              </option>
            ))}
          </SelectField>

          <SelectField
            id="fur-type"
            label="Tipo de trámite"
            value={typeId}
            disabled={!family}
            onChange={applyTypeDefaults}
          >
            <option value="">Selecciona un tipo</option>
            {typesInFamily.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name} ({t.code})
              </option>
            ))}
          </SelectField>

          <p className="md:col-span-2 text-sm text-[#5B6475] dark:text-white/70">
            Casillas = tipo + prenda + transformaciones (reglas numeral 3).
          </p>

          {selectedType ? (
            <FurResultGuide
              code={selectedType.code}
              family={selectedType.family}
              prenda={prenda}
              color={cambioColor}
              carroceria={cambioCarroceria}
              combustible={cambioCombustible}
              blindaje={blindaje}
            />
          ) : null}

          <div className="flex flex-col gap-1.5">
            <SelectField
              id="fur-seller"
              label="Vendedor"
              value={sellerKind}
              disabled={!canChooseSeller}
              onChange={(v) => setSellerKind(v as FurPersonKind)}
            >
              {PERSONS.map((p) => (
                <option key={p.value} value={p.value}>
                  {p.label}
                </option>
              ))}
            </SelectField>
            {!canChooseSeller && (
              <p className="text-xs text-[#59677D] dark:text-white/55" data-testid="fur-seller-hint">
                En este tipo el FUR no pinta vendedor: solo el radicador / comprador.
              </p>
            )}
          </div>

          <SelectField
            id="fur-buyer"
            label="Comprador"
            value={buyerKind}
            onChange={(v) => setBuyerKind(v as FurPersonKind)}
          >
            {PERSONS.map((p) => (
              <option key={p.value} value={p.value}>
                {p.label}
              </option>
            ))}
          </SelectField>

          <SelectField
            id="fur-vehicle"
            label="Tipo de vehículo"
            value={vehicleKind}
            onChange={(v) => setVehicleKind(v as FurVehicleKind)}
          >
            <option value="">Selecciona un vehículo</option>
            {VEHICLES.map((v) => (
              <option key={v.value} value={v.value}>
                {v.label}
              </option>
            ))}
          </SelectField>

          <SelectField
            id="fur-prenda"
            label="Prenda"
            value={prenda}
            disabled={inferredLocks.lockPrenda}
            onChange={(v) => setPrenda(v as FurPrendaKind)}
          >
            {PRENDA.map((p) => (
              <option key={p.value} value={p.value}>
                {p.label}
              </option>
            ))}
          </SelectField>

          <fieldset className="md:col-span-2 flex flex-col gap-2 rounded-xl border border-[#DFE5ED] p-3 dark:border-white/10">
            <legend className="px-1 text-sm font-semibold text-[#162244] dark:text-white">
              Transformaciones
            </legend>
            <div className="grid gap-2 sm:grid-cols-2">
              <CheckboxField
                id="fur-color"
                label="Cambio de color"
                checked={cambioColor}
                disabled={inferredLocks.lockColor}
                onChange={setCambioColor}
              />
              <CheckboxField
                id="fur-combustible"
                label="Cambio de combustible"
                checked={cambioCombustible}
                disabled={inferredLocks.lockCombustible}
                onChange={setCambioCombustible}
              />
              <CheckboxField
                id="fur-carroceria"
                label="Cambio de carrocería"
                checked={cambioCarroceria}
                disabled={inferredLocks.lockCarroceria}
                onChange={setCambioCarroceria}
              />
              <CheckboxField
                id="fur-blindaje"
                label="Blindaje"
                checked={blindaje}
                disabled={inferredLocks.lockBlindaje}
                onChange={setBlindaje}
              />
            </div>
          </fieldset>

          <div className="md:col-span-2 flex items-end">
            <button
              type="submit"
              disabled={!canSimulate}
              aria-busy={previewLoading}
              className="w-full rounded-xl px-4 py-2.5 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
            >
              Simular FUR
            </button>
          </div>
        </form>
      </UiStateBoundary>
    </div>
  );
}
