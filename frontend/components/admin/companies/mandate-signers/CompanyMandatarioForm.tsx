"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { avisoDeNuevaValidacion } from "@/lib/plataforma/mandatario-validacion";
import {
  FORMAS_DE_FIRMA,
  MODELOS_MANDATARIO,
  NOMBRE_FORMATO_EN_BLANCO,
  TIPOS_DE_VIGENCIA,
  camposDePerfil,
  campoDeError,
  perfilInicial,
  validarPerfil,
  type CampoMandatario,
  type ErroresMandatario,
} from "@/lib/plataforma/mandatario-modelo";
import { ApiError, ApiValidationError } from "@/lib/api/types";
import { SignatureVaultSelector } from "@/components/admin/companies/legal-representatives/SignatureVaultSelector";
import { MandatarioIdentidadBlock } from "./MandatarioIdentidadBlock";
import {
  MandatarioCompaniasAsociadas,
  type AsociadaSeleccionada,
  type FuenteAsociadas,
  precargarAsociadas,
} from "./MandatarioCompaniasAsociadas";
import type {
  CompanyMandateSignerInput,
  CompanyTransitOfficeOption,
  MandateSigner,
  MandateSignerIdentityResend,
  MandateSignerSaved,
  SignatureMethod,
  SignerModel,
  ValidityKind,
} from "@/lib/api/admin-mandate-signers";

const DOC_TYPES = ["CC", "CE", "PAS", "NIT"];

/**
 * HU #11202 (AC1/AC2/AC3) — alta y edición del mandatario desde el configurador de la compañía. Los
 * organismos son un multiselect de los que la compañía tiene habilitados: ofrecer otros sería ofrecer
 * un destino donde no puede radicar.
 */
export function CompanyMandatarioForm({
  variant = "company",
  tenantId,
  networkHeadId,
  offices,
  asociadas,
  ownerCompanyIds = [],
  editing,
  initialOfficeIds,
  restrictToOfficeIds,
  overlayClassName = "z-50",
  onCancel,
  onSubmit,
  onResend,
}: {
  /**
   * `hub` (HU #13124): alta desde el hub del organismo. No lee rutas de la compañía (sin selector del
   * baúl, el servidor resuelve la firma) y el organismo queda fijo.
   * HU #13132: modelo, forma de firma y vigencia igual que en la compañía.
   */
  variant?: "company" | "hub";
  tenantId?: string;
  networkHeadId?: string | null;
  offices: CompanyTransitOfficeOption[];
  /**
   * HU #13181 — de dónde salen las compañías asociables según el perfil: `ot` (OT y Super Admin:
   * todas, con búsqueda) o `hijas` (Admin de Compañía: solo las suyas). Por defecto: el hub usa `ot`
   * con su organismo y la compañía usa `hijas`.
   */
  asociadas?: FuenteAsociadas;
  /** Compañía propia del mandatario: no se ofrece en la lista (el servidor la rechazaría). */
  ownerCompanyIds?: string[];
  editing: MandateSigner | null;
  /** En alta desde el hub OT, premarca este organismo. */
  initialOfficeIds?: string[];
  /**
   * Si se informa, el listado de organismos solo muestra esos ids. El panel de la compañía no lo
   * pasa: ahí se siguen viendo todos los OT habilitados.
   */
  restrictToOfficeIds?: string[];
  /** Overlay del modal. Por defecto `z-50`; desde un OtSidePanel hay que subir (p. ej. `z-[80]`). */
  overlayClassName?: string;
  onCancel: () => void;
  onSubmit: (input: CompanyMandateSignerInput) => Promise<MandateSignerSaved>;
  /** HU #13248 — «Reenviar validación» de la ficha (al editar). Sin esta prop no se ofrece el botón. */
  onResend?: (signer: MandateSigner) => Promise<MandateSignerIdentityResend>;
}) {
  const inicial = perfilInicial(editing);
  const [signerModel, setSignerModel] = useState<SignerModel>(inicial.model);
  const [fullName, setFullName] = useState(
    editing?.signerModel === "formato_blanco" ? "" : (editing?.fullName ?? ""),
  );
  const [documentType, setDocumentType] = useState(editing?.documentType ?? "CC");
  const [documentNumber, setDocumentNumber] = useState(editing?.documentNumber ?? "");
  const [email, setEmail] = useState(editing?.email ?? "");
  const [selected, setSelected] = useState<string[]>(
    editing?.transitOfficeIds ?? initialOfficeIds ?? [],
  );
  // HU #13132 — forma de firma y vigencia de la Persona natural.
  const [signatureMethod, setSignatureMethod] = useState<SignatureMethod | null>(inicial.method);
  const [validityKind, setValidityKind] = useState<ValidityKind>(inicial.validityKind);
  const [validFrom, setValidFrom] = useState(inicial.validFrom);
  const [validTo, setValidTo] = useState(inicial.validTo);
  // Firma del baúl del mandatario (solo con forma «baúl»).
  const [signatureVaultId, setSignatureVaultId] = useState<string | null>(inicial.signatureVaultId);
  // HU #13181 — compañías asociadas (una sola selección que aplica a los organismos elegidos).
  const [asociadasSel, setAsociadasSel] = useState<Record<string, AsociadaSeleccionada>>(() =>
    precargarAsociadas(editing?.officeCompanies),
  );
  const [erroresAsociadas, setErroresAsociadas] = useState<Record<string, string>>({});
  // Verdadero cuando el Admin de Compañía no tiene hijas: no hay lista y el mandatario es solo suyo.
  const [sinRed, setSinRed] = useState(false);
  const isHub = variant === "hub";
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<ErroresMandatario>({});

  const esNatural = signerModel === "natural";
  const esJuridica = signerModel === "juridica";
  // AC5: se avisa antes de guardar que cambiar de Persona natural descarta forma de firma y vigencia.
  const descartaDatos = editing != null && (editing.signerModel ?? "natural") === "natural" && !esNatural;

  const conBiometria = esNatural && signatureMethod === "biometria";
  const avisoValidacion = avisoDeNuevaValidacion({
    editing,
    metodo: signatureMethod,
    esNatural,
    tipoDocumento: documentType,
    numeroDocumento: documentNumber,
  });

  const clearField = (campo: CampoMandatario) => {
    setError(null);
    setFieldErrors((prev) => {
      if (!prev[campo]) return prev;
      const next = { ...prev };
      delete next[campo];
      return next;
    });
  };

  const toggleOffice = (id: string) => {
    setError(null);
    setSelected((prev) => {
      const quitando = prev.includes(id);
      return quitando ? prev.filter((x) => x !== id) : [...prev, id];
    });
  };

  const fuenteAsociadas: FuenteAsociadas | null =
    asociadas ??
    (isHub
      ? (() => {
          const ot = restrictToOfficeIds?.[0] ?? initialOfficeIds?.[0];
          return ot ? { modo: "ot" as const, transitOfficeId: ot } : null;
        })()
      : tenantId
        ? { modo: "hijas" as const, tenantId, networkHeadId }
        : null);

  const visibleOffices =
    restrictToOfficeIds && restrictToOfficeIds.length > 0
      ? offices.filter((o) => restrictToOfficeIds.includes(o.transitOfficeId))
      : offices;

  const handleSave = async () => {
    const errores: ErroresMandatario = {};
    if (esNatural || esJuridica) {
      if (!fullName.trim()) {
        errores.fullName = esJuridica ? "Escribe el nombre de la entidad." : "Escribe el nombre completo.";
      }
      if (!documentNumber.trim()) {
        errores.documentNumber = esJuridica ? "Escribe el NIT." : "Escribe el número de documento.";
      }
    }
    // HU #13248 — con validación de identidad el enlace se envía al correo: es obligatorio.
    if (esNatural && signatureMethod === "biometria" && !email.trim()) {
      errores.email = "Escribe el correo: ahí enviamos el enlace de validación.";
    }
    if (selected.length === 0) {
      errores.offices = "Elige al menos un organismo de tránsito donde aplique el mandatario.";
    }
    Object.assign(
      errores,
      validarPerfil(
        {
          model: signerModel,
          method: signatureMethod,
          validityKind,
          validFrom,
          validTo,
          signatureVaultId,
        },
        { exigeSelectorBaul: !isHub },
      ),
    );
    setFieldErrors(errores);
    setError(null);
    setErroresAsociadas({});
    if (Object.keys(errores).length > 0) return;

    const formatoBlanco = signerModel === "formato_blanco";
    const conBaul = esNatural && signatureMethod === "baul" && !isHub;
    setSaving(true);
    try {
      await onSubmit({
        fullName: formatoBlanco ? NOMBRE_FORMATO_EN_BLANCO : fullName.trim(),
        documentType: esJuridica ? "NIT" : formatoBlanco ? "CC" : documentType,
        documentNumber: formatoBlanco ? null : documentNumber.trim(),
        // El correo solo aplica a Persona natural: con otros modelos el servidor responde 422.
        email: !esNatural || email.trim() === "" ? null : email.trim(),
        transitOfficeIds: selected,
        ...camposDePerfil({
          model: signerModel,
          method: signatureMethod,
          validityKind,
          validFrom,
          validTo,
          signatureVaultId,
        }),
        signatureVaultId: isHub ? undefined : conBaul ? signatureVaultId : null,
        // Sin lista (Admin de Compañía sin red) no se envía nada: aplica solo a su compañía.
        officeCompanies:
          fuenteAsociadas && !sinRed
            ? selected.map((transitOfficeId) => ({
                transitOfficeId,
                associatedCompanyTenantIds: Object.keys(asociadasSel),
              }))
            : undefined,
      });
    } catch (err) {
      const { campos, general, porCompania } = repartirError(err, isHub);
      setFieldErrors(campos);
      setErroresAsociadas(porCompania);
      setError(general);
    } finally {
      setSaving(false);
    }
  };

  const inputClass =
    "w-full rounded-xl border bg-white px-3 py-2 text-xs outline-none focus:border-[#557EFF] dark:bg-[#0B0F14]";

  return (
    <div
      className={`fixed inset-0 ${overlayClassName} grid place-items-center bg-black/40 px-4 backdrop-blur-sm`}
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
          <fieldset>
            <legend className="mb-1.5 block text-xs font-semibold">Modelo del mandatario</legend>
            <div className="flex flex-wrap gap-2" role="radiogroup" aria-label="Modelo del mandatario">
              {MODELOS_MANDATARIO.map((m) => (
                <RadioChip
                  key={m.value}
                  name="mandatario-modelo"
                  label={m.label}
                  checked={signerModel === m.value}
                  onChange={() => {
                    setSignerModel(m.value);
                    setError(null);
                    setFieldErrors({});
                  }}
                />
              ))}
            </div>
            {signerModel === "formato_blanco" && (
              <p className="mt-1 text-[11px] leading-tight opacity-70" data-testid="mandatario-formato-blanco-nota">
                El sistema solo entrega el PDF sin firma. No se piden datos de la persona.
              </p>
            )}
            {descartaDatos && (
              <p
                className="mt-1 text-[11px] leading-tight"
                style={{ color: "#8a6000" }}
                role="status"
                data-testid="mandatario-cambio-modelo-aviso"
              >
                Al guardar se descartan la forma de firma y la vigencia de la Persona natural.
              </p>
            )}
          </fieldset>

          {signerModel !== "formato_blanco" && (
            <div>
              <label htmlFor="mandatario-nombre" className="mb-1.5 block text-xs font-semibold">
                {esJuridica ? "Nombre de la entidad" : "Nombre completo"}
              </label>
              <input
                id="mandatario-nombre"
                type="text"
                value={fullName}
                onChange={(e) => {
                  setFullName(e.target.value);
                  clearField("fullName");
                }}
                className={inputClass}
                aria-invalid={fieldErrors.fullName ? true : undefined}
                aria-describedby={fieldErrors.fullName ? "mandatario-nombre-error" : undefined}
              />
              <FieldError id="mandatario-nombre-error" message={fieldErrors.fullName} />
            </div>
          )}

          {signerModel !== "formato_blanco" && (
            <div className={esJuridica ? undefined : "grid gap-3 sm:grid-cols-2"}>
              {esNatural && (
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
              )}
              <div>
                <label htmlFor="mandatario-doc" className="mb-1.5 block text-xs font-semibold">
                  {esJuridica ? "NIT" : "Número de documento"}
                </label>
                <input
                  id="mandatario-doc"
                  type="text"
                  value={documentNumber}
                  onChange={(e) => {
                    setDocumentNumber(e.target.value);
                    clearField("documentNumber");
                  }}
                  className={inputClass}
                  aria-invalid={fieldErrors.documentNumber ? true : undefined}
                  aria-describedby={fieldErrors.documentNumber ? "mandatario-doc-error" : undefined}
                />
                <FieldError id="mandatario-doc-error" message={fieldErrors.documentNumber} />
              </div>
            </div>
          )}

          {esNatural && (
            <>
              <div>
                <label htmlFor="mandatario-email" className="mb-1.5 block text-xs font-semibold">
                  Correo{conBiometria ? " (obligatorio)" : " (opcional)"}
                </label>
                <input
                  id="mandatario-email"
                  type="email"
                  required={conBiometria}
                  aria-required={conBiometria ? true : undefined}
                  value={email}
                  onChange={(e) => {
                    setEmail(e.target.value);
                    clearField("email");
                  }}
                  className={inputClass}
                  aria-invalid={fieldErrors.email ? true : undefined}
                  aria-describedby={fieldErrors.email ? "mandatario-email-error" : undefined}
                />
                <FieldError id="mandatario-email-error" message={fieldErrors.email} />
                <p className="mt-1 text-[11px] leading-tight opacity-70">
                  {conBiometria
                    ? "Aquí enviamos el enlace para que la persona valide su identidad."
                    : "Es un dato de contacto."}
                </p>
              </div>

              {/* Solo al EDITAR: en el alta el mandatario aún no tiene id contra el que consultar. */}
              {editing && (
                <MandatarioIdentidadBlock
                  signer={editing}
                  onResend={onResend ? () => onResend(editing) : undefined}
                />
              )}

              <fieldset>
                <legend className="mb-1.5 block text-xs font-semibold">Forma de firma</legend>
                <div className="flex flex-wrap gap-2" role="radiogroup" aria-label="Forma de firma">
                  {FORMAS_DE_FIRMA.map((f) => (
                    <RadioChip
                      key={f.value}
                      name="mandatario-forma-firma"
                      label={f.label}
                      checked={signatureMethod === f.value}
                      onChange={() => {
                        setSignatureMethod(f.value);
                        clearField("signatureMethod");
                        clearField("signatureVaultId");
                      }}
                    />
                  ))}
                </div>
                <FieldError id="mandatario-forma-firma-error" message={fieldErrors.signatureMethod} />
                {signatureMethod === "biometria" && (
                  <p className="mt-1 text-[11px] leading-tight opacity-70">
                    La persona valida su identidad con un enlace que le llega al correo.
                  </p>
                )}
                {avisoValidacion && (
                  <p
                    className="mt-1 text-[11px] leading-tight"
                    style={{ color: "#8a6000" }}
                    role="status"
                    data-testid="mandatario-aviso-validacion"
                  >
                    {avisoValidacion}
                  </p>
                )}
                {signatureMethod === "baul" && isHub && (
                  <p
                    className="mt-1 text-[11px] leading-tight opacity-70"
                    data-testid="mandatario-hub-firma-nota"
                  >
                    Se usa la firma que la persona tenga vigente en el baúl de la empresa.
                  </p>
                )}
              </fieldset>

              {signatureMethod === "baul" && !isHub && (
                <div>
                  <label htmlFor="lr-sig-vault" className="mb-1.5 block text-xs font-semibold">
                    Firma del baúl
                  </label>
                  <SignatureVaultSelector
                    tenantId={tenantId ?? ""}
                    networkHeadId={networkHeadId}
                    documentType={documentType}
                    documentNumber={documentNumber}
                    value={signatureVaultId}
                    onChange={(id) => {
                      setSignatureVaultId(id);
                      clearField("signatureVaultId");
                    }}
                    fullName={fullName.trim() === "" ? undefined : fullName.trim()}
                  />
                  <FieldError id="mandatario-baul-error" message={fieldErrors.signatureVaultId} />
                </div>
              )}

              <fieldset>
                <legend className="mb-1.5 block text-xs font-semibold">Vigencia</legend>
                <div className="flex flex-wrap gap-2" role="radiogroup" aria-label="Vigencia">
                  {TIPOS_DE_VIGENCIA.map((v) => (
                    <RadioChip
                      key={v.value}
                      name="mandatario-vigencia"
                      label={v.label}
                      checked={validityKind === v.value}
                      onChange={() => {
                        setValidityKind(v.value);
                        clearField("validFrom");
                        clearField("validTo");
                      }}
                    />
                  ))}
                </div>
                {validityKind === "range" && (
                  <div className="mt-2 grid gap-3 sm:grid-cols-2">
                    <div>
                      <label htmlFor="mandatario-valid-from" className="mb-1.5 block text-xs font-semibold">
                        Fecha de inicio
                      </label>
                      <input
                        id="mandatario-valid-from"
                        type="date"
                        value={validFrom}
                        onChange={(e) => {
                          setValidFrom(e.target.value);
                          clearField("validFrom");
                          clearField("validTo");
                        }}
                        className={inputClass}
                        aria-invalid={fieldErrors.validFrom ? true : undefined}
                        aria-describedby={fieldErrors.validFrom ? "mandatario-valid-from-error" : undefined}
                      />
                      <FieldError id="mandatario-valid-from-error" message={fieldErrors.validFrom} />
                    </div>
                    <div>
                      <label htmlFor="mandatario-valid-to" className="mb-1.5 block text-xs font-semibold">
                        Fecha de fin
                      </label>
                      <input
                        id="mandatario-valid-to"
                        type="date"
                        value={validTo}
                        min={validFrom || undefined}
                        onChange={(e) => {
                          setValidTo(e.target.value);
                          clearField("validTo");
                        }}
                        className={inputClass}
                        aria-invalid={fieldErrors.validTo ? true : undefined}
                        aria-describedby={fieldErrors.validTo ? "mandatario-valid-to-error" : undefined}
                      />
                      <FieldError id="mandatario-valid-to-error" message={fieldErrors.validTo} />
                    </div>
                  </div>
                )}
                <FieldError id="mandatario-vigencia-error" message={fieldErrors.validityKind} />
              </fieldset>
            </>
          )}

          <fieldset>
            <legend className="mb-1.5 block text-xs font-semibold">
              Organismos donde aplica
            </legend>
            <div className="space-y-1.5 rounded-xl border p-3">
              {visibleOffices.map((o) => (
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
                </div>
              ))}
            </div>
            <p className="mt-1 text-[11px] leading-tight opacity-70">
              {isHub ? (
                "El mandatario se registra en este organismo."
              ) : (
                "Solo se listan los organismos habilitados para esta compañía. Al editar, quitar uno retira al mandatario de ese organismo y lo deja en los demás."
              )}
            </p>

            <FieldError id="mandatario-offices-error" message={fieldErrors.offices} />
          </fieldset>

          {fuenteAsociadas ? (
            <fieldset>
              <legend className="mb-1.5 block text-xs font-semibold">Compañías asociadas</legend>
              <MandatarioCompaniasAsociadas
                fuente={fuenteAsociadas}
                seleccion={asociadasSel}
                onChange={(next) => {
                  setAsociadasSel(next);
                  setErroresAsociadas({});
                  setError(null);
                }}
                excluirIds={ownerCompanyIds}
                errores={erroresAsociadas}
                onSinRed={setSinRed}
              />
            </fieldset>
          ) : null}

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

function RadioChip({
  name,
  label,
  checked,
  onChange,
}: {
  name: string;
  label: string;
  checked: boolean;
  onChange: () => void;
}) {
  return (
    <label
      className="flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-2 text-xs"
      style={checked ? { borderColor: "#557EFF" } : undefined}
    >
      <input
        type="radio"
        name={name}
        checked={checked}
        onChange={onChange}
        className="h-4 w-4 accent-[#557EFF]"
      />
      {label}
    </label>
  );
}

function FieldError({ id, message }: { id: string; message?: string }) {
  if (!message) return null;
  return (
    <p id={id} className="mt-1 text-[11px] leading-tight" style={{ color: "#E5484D" }} role="alert">
      {message}
    </p>
  );
}

/**
 * Reparte el error del servidor: los 422 con `field` conocido van junto a su campo (AC4); el resto
 * queda como mensaje general. Sin códigos ni detalles técnicos.
 */
function repartirError(
  err: unknown,
  isHub: boolean,
): { campos: ErroresMandatario; general: string | null; porCompania: Record<string, string> } {
  const campos: ErroresMandatario = {};
  const porCompania: Record<string, string> = {};
  if (err instanceof ApiValidationError) {
    const generales: string[] = [];
    for (const e of err.errors) {
      // HU #13179 — el 422 de una compañía asociada trae su id de tenant en `value`.
      if (e.field?.replace(/[^a-z]/gi, "").toLowerCase() === "associatedcompanytenantids" && e.value) {
        porCompania[e.value] = e.message;
        continue;
      }
      const campo = campoDeError(e.field);
      if (campo && !campos[campo]) campos[campo] = e.message;
      else generales.push(e.message);
    }
    const msg = generales.join(" ").trim();
    if (Object.keys(campos).length > 0 || Object.keys(porCompania).length > 0) {
      return { campos, general: msg || null, porCompania };
    }
    return {
      campos,
      porCompania,
      general: msg || "No se pudo guardar el mandatario. Revisa los datos e intenta de nuevo.",
    };
  }
  if (err instanceof ApiError) {
    // HU #13179 — el Admin de Compañía envió una compañía que no es su hija: no se guardó nada.
    if (
      err.status === 403 &&
      (err.body as { code?: string } | undefined)?.code === "compania_asociada_fuera_de_alcance"
    ) {
      return {
        campos,
        porCompania,
        general: err.message || "Una de las compañías no está dentro de tu alcance. No se guardó nada.",
      };
    }
    if (err.status === 403) {
      return {
        campos,
        porCompania,
        general:
          "No tienes permiso para registrar mandatarios. Solo el administrador del organismo puede hacerlo.",
      };
    }
    // HU #13248 — errores propios de la validación de identidad: el formulario conserva lo escrito.
    if ((err.body as { code?: string } | undefined)?.code === "mandatario_no_requiere_validacion") {
      return {
        campos,
        porCompania,
        general: "Este mandatario no requiere validación de identidad. Revisa la forma de firma.",
      };
    }
    if (err.status === 502) {
      return {
        campos,
        porCompania,
        general: "No pudimos enviar la validación de identidad. Revisa el correo e intenta de nuevo.",
      };
    }
    if (err.status === 422 && err.message.trim()) return { campos, porCompania, general: err.message };
  }
  return {
    campos,
    porCompania,
    general: isHub
      ? "No se pudo registrar el mandatario. Intenta de nuevo."
      : "No se pudo guardar el mandatario.",
  };
}
