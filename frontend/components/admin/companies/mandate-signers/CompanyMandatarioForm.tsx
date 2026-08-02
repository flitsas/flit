"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { ApiValidationError } from "@/lib/api/types";
import { SignatureVaultSelector } from "@/components/admin/companies/legal-representatives/SignatureVaultSelector";
import { MandatarioIdentidadBlock } from "./MandatarioIdentidadBlock";
import type { RepresentedCompanyOption } from "@/lib/api/admin-mandate-signers";
import type {
  CompanyMandateSignerInput,
  CompanyTransitOfficeOption,
  MandateSigner,
  MandateSignerSaved,
} from "@/lib/api/admin-mandate-signers";

const DOC_TYPES = ["CC", "CE", "PAS", "NIT"];

/**
 * HU #11202 (AC1/AC2/AC3) — alta y edición del mandatario desde el configurador de la compañía. Los
 * organismos son un multiselect de los que la compañía tiene habilitados: ofrecer otros sería ofrecer
 * un destino donde no puede radicar.
 */
export function CompanyMandatarioForm({
  tenantId,
  offices,
  companies = [],
  editing,
  onCancel,
  onSubmit,
  onIdentityChanged,
}: {
  tenantId: string;
  offices: CompanyTransitOfficeOption[];
  /** Empresas representadas de la compañía, para acotar para quién firma en cada organismo. */
  companies?: RepresentedCompanyOption[];
  editing: MandateSigner | null;
  onCancel: () => void;
  onSubmit: (input: CompanyMandateSignerInput) => Promise<MandateSignerSaved>;
  /** Refresca el listado tras una acción de identidad, que cambia el estado del mandatario. */
  onIdentityChanged?: () => void;
}) {
  const [fullName, setFullName] = useState(editing?.fullName ?? "");
  const [documentType, setDocumentType] = useState(editing?.documentType ?? "CC");
  const [documentNumber, setDocumentNumber] = useState(editing?.documentNumber ?? "");
  const [email, setEmail] = useState(editing?.email ?? "");
  const [selected, setSelected] = useState<string[]>(editing?.transitOfficeIds ?? []);
  // Organismos donde esta persona firma A MANO. Va por organismo y no por persona porque la misma
  // puede firmar a mano ante uno y electrónicamente ante otro.
  const [fisicos, setFisicos] = useState<string[]>(editing?.physicalSignatureOfficeIds ?? []);
  // Firma del baúl del mandatario. La columna existía desde la HU #10910 pero nunca se poblaba: el
  // trámite la resolvía por documento y esta referencia quedaba siempre nula.
  const [signatureVaultId, setSignatureVaultId] = useState<string | null>(
    editing?.signatureVaultId ?? null,
  );
  // Empresas por organismo. La ausencia de entrada para un organismo significa "todas": es como se
  // comportan los mandatarios que ya existen, y por eso el estado arranca solo con lo que hay guardado.
  const [empresasPorOt, setEmpresasPorOt] = useState<Record<string, string[]>>(() =>
    Object.fromEntries(
      (editing?.officeCompanies ?? []).map((o) => [o.transitOfficeId, o.representedCompanyIds]),
    ),
  );

  const toggleEmpresa = (officeId: string, companyId: string) => {
    setEmpresasPorOt((prev) => {
      const actuales = prev[officeId] ?? [];
      const siguientes = actuales.includes(companyId)
        ? actuales.filter((x) => x !== companyId)
        : [...actuales, companyId];
      // Sin ninguna marcada se borra la entrada: la ausencia es lo que significa "todas", y dejar un
      // arreglo vacío diría "ninguna", que dejaría al mandatario sin poder firmar nada.
      const next = { ...prev };
      if (siguientes.length === 0) delete next[officeId];
      else next[officeId] = siguientes;
      return next;
    });
  };
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const toggleOffice = (id: string) => {
    setError(null);
    setSelected((prev) => {
      const quitando = prev.includes(id);
      // Al retirar el organismo se retira también su marca de firma física: dejarla colgando haría
      // que al volver a añadirlo reapareciera una firma a mano que nadie pidió.
      if (quitando) {
        setFisicos((f) => f.filter((x) => x !== id));
        setEmpresasPorOt((prev) => {
          const next = { ...prev };
          delete next[id];
          return next;
        });
      }
      return quitando ? prev.filter((x) => x !== id) : [...prev, id];
    });
  };

  const toggleFisico = (id: string) => {
    setError(null);
    setFisicos((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  };

  const handleSave = async () => {
    if (!fullName.trim() || !documentNumber.trim()) {
      setError("El nombre y el número de documento son obligatorios.");
      return;
    }
    if (selected.length === 0) {
      setError("Elige al menos un organismo de tránsito donde aplique el mandatario.");
      return;
    }

    setSaving(true);
    setError(null);
    try {
      await onSubmit({
        fullName: fullName.trim(),
        documentType,
        documentNumber: documentNumber.trim(),
        email: email.trim() === "" ? null : email.trim(),
        transitOfficeIds: selected,
        physicalSignatureOfficeIds: fisicos,
        signatureVaultId,
        officeCompanies: Object.entries(empresasPorOt)
          // Solo de los organismos que siguen elegidos: retirar uno se lleva su acotación.
          .filter(([officeId]) => selected.includes(officeId))
          .map(([transitOfficeId, representedCompanyIds]) => ({
            transitOfficeId,
            representedCompanyIds,
          })),
      });
    } catch (err) {
      setError(
        err instanceof ApiValidationError
          ? err.errors.map((e) => e.message).join(" ")
          : "No se pudo guardar el mandatario.",
      );
    } finally {
      setSaving(false);
    }
  };

  const inputClass =
    "w-full rounded-xl border bg-white px-3 py-2 text-xs outline-none focus:border-[#557EFF] dark:bg-[#0B0F14]";

  return (
    <div
      className="fixed inset-0 z-50 grid place-items-center bg-black/40 px-4 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-label={editing ? "Editar mandatario" : "Registrar mandatario"}
    >
      <div className="flex max-h-[85vh] w-full max-w-lg flex-col rounded-2xl border bg-white p-6 dark:bg-[#0B0F14]">
        <div className="mb-3 flex items-start justify-between">
          <h3 className="text-sm font-bold">
            {editing ? "Editar mandatario" : "Registrar mandatario"}
          </h3>
          <button type="button" onClick={onCancel} aria-label="Cerrar">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="flex-1 space-y-3 overflow-y-auto">
          <div>
            <label htmlFor="mandatario-nombre" className="mb-1.5 block text-xs font-semibold">
              Nombre completo
            </label>
            <input
              id="mandatario-nombre"
              type="text"
              value={fullName}
              onChange={(e) => setFullName(e.target.value)}
              className={inputClass}
            />
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <div>
              <label htmlFor="mandatario-tipo-doc" className="mb-1.5 block text-xs font-semibold">
                Tipo de documento
              </label>
              <select
                id="mandatario-tipo-doc"
                value={documentType}
                onChange={(e) => setDocumentType(e.target.value)}
                className={inputClass}
              >
                {DOC_TYPES.map((t) => (
                  <option key={t} value={t}>
                    {t}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label htmlFor="mandatario-doc" className="mb-1.5 block text-xs font-semibold">
                Número de documento
              </label>
              <input
                id="mandatario-doc"
                type="text"
                value={documentNumber}
                onChange={(e) => setDocumentNumber(e.target.value)}
                className={inputClass}
              />
            </div>
          </div>

          <div>
            <label htmlFor="mandatario-email" className="mb-1.5 block text-xs font-semibold">
              Correo
            </label>
            <input
              id="mandatario-email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className={inputClass}
            />
            <p className="mt-1 text-[11px] leading-tight opacity-70">
              Con correo se le envía la validación de identidad al registrarlo. Si la persona ya tiene
              una vigente, se reutiliza y no se le vuelve a escribir.
            </p>
          </div>

          {/* Solo al EDITAR: en el alta el mandatario aún no tiene id contra el que actuar, y el envío
              inicial ya lo dispara el propio alta cuando hay correo. */}
          {editing && (
            <MandatarioIdentidadBlock
              tenantId={tenantId}
              signer={editing}
              onRefresh={() => onIdentityChanged?.()}
            />
          )}

          {/* Igual que en el panel del representante legal, pero sin escrituras: el mandatario no las
              necesita. Sirve para elegir su firma o capturarla ahí mismo si aún no tiene. */}
          <div>
            <label htmlFor="lr-sig-vault" className="mb-1.5 block text-xs font-semibold">
              Firma del baúl <span className="font-normal opacity-60">(opcional)</span>
            </label>
            <SignatureVaultSelector
              tenantId={tenantId}
              documentType={documentType}
              documentNumber={documentNumber}
              value={signatureVaultId}
              onChange={setSignatureVaultId}
              fullName={fullName.trim() === "" ? undefined : fullName.trim()}
            />
            <p className="mt-1 text-[11px] leading-tight opacity-70">
              Con firma del baúl vigente, el mandato la estampa. Sin ella se usa el sello de su
              validación de identidad, y sin ninguna de las dos queda la línea para firmar a mano.
            </p>
          </div>

          <fieldset>
            <legend className="mb-1.5 block text-xs font-semibold">
              Organismos donde aplica
            </legend>
            <div className="space-y-1.5 rounded-xl border p-3">
              {offices.map((o) => (
                <div key={o.transitOfficeId}>
                  <label className="flex items-center gap-2 text-xs">
                    <input
                      type="checkbox"
                      checked={selected.includes(o.transitOfficeId)}
                      onChange={() => toggleOffice(o.transitOfficeId)}
                      aria-label={o.name}
                    />
                    <span>
                      {o.name}
                      {o.code && <span className="opacity-70"> · {o.code}</span>}
                    </span>
                  </label>
                  {/* Acotación por empresa, solo donde el mandatario aplica. Sin ninguna marcada
                      firma para TODAS las empresas de ese organismo. */}
                  {selected.includes(o.transitOfficeId) && companies.length > 0 && (
                    <div className="mt-1 ml-6">
                      <p className="text-[11px] opacity-70">
                        {(empresasPorOt[o.transitOfficeId]?.length ?? 0) === 0
                          ? "Firma para todas las empresas de este organismo."
                          : `Firma solo para ${empresasPorOt[o.transitOfficeId]!.length} empresa(s):`}
                      </p>
                      <div className="mt-1 space-y-1">
                        {companies.map((e) => (
                          <label
                            key={`${o.transitOfficeId}-${e.id}`}
                            className="flex items-center gap-2 text-[11px]"
                          >
                            <input
                              type="checkbox"
                              checked={(empresasPorOt[o.transitOfficeId] ?? []).includes(e.id)}
                              onChange={() => toggleEmpresa(o.transitOfficeId, e.id)}
                              aria-label={`${e.name} (${e.documentNumber}) en ${o.name}`}
                            />
                            <span>
                              {e.name}
                              <span className="opacity-70"> · NIT {e.documentNumber}</span>
                            </span>
                          </label>
                        ))}
                      </div>
                    </div>
                  )}

                  {/* Solo tiene sentido marcar la firma a mano donde el mandatario aplica. */}
                  {selected.includes(o.transitOfficeId) && (
                    <label className="mt-1 ml-6 flex items-center gap-2 text-[11px] opacity-80">
                      <input
                        type="checkbox"
                        checked={fisicos.includes(o.transitOfficeId)}
                        onChange={() => toggleFisico(o.transitOfficeId)}
                        aria-label={`Firma de forma física · ${o.name}`}
                      />
                      <span>Firma de forma física</span>
                    </label>
                  )}
                </div>
              ))}
            </div>
            <p className="mt-1 text-[11px] leading-tight opacity-70">
              Solo se listan los organismos habilitados para esta compañía. Al editar, quitar uno
              retira al mandatario de ese organismo y lo deja en los demás.
              <br />
              Con «firma de forma física», el contrato de mandato de ese organismo deja la línea con sus
              datos debajo para firmarla a mano, en vez de estampar su firma del baúl o su sello de
              identidad.
            </p>
          </fieldset>

          {error && (
            <p className="text-[11px] leading-tight" style={{ color: "#E5484D" }} role="alert">
              {error}
            </p>
          )}
        </div>

        <div className="mt-4 flex justify-end gap-2">
          <button
            type="button"
            onClick={onCancel}
            className="rounded-xl border px-4 py-2 text-xs font-semibold"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => void handleSave()}
            disabled={saving}
            className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
            style={{ background: "#557EFF" }}
          >
            {saving ? "Guardando…" : "Guardar"}
          </button>
        </div>
      </div>
    </div>
  );
}
