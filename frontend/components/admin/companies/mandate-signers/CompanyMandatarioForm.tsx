"use client";

import { useState, type ReactNode } from "react";
import { Building2, FileText, ShieldCheck, User, Vault } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import {
  MultiSelectBuscable,
  type OpcionSeleccionable,
} from "@/components/atom/MultiSelectBuscable";
import {
  avisoDeNuevaValidacion,
  consecuenciaDeGuardar,
  disparoDeValidacion,
} from "@/lib/plataforma/mandatario-validacion";
import {
  NOMBRE_FORMATO_EN_BLANCO,
  camposDePerfil,
  campoDeError,
  perfilInicial,
  validarPerfil,
  type CampoMandatario,
  type ErroresMandatario,
} from "@/lib/plataforma/mandatario-modelo";
import { ApiError, ApiValidationError } from "@/lib/api/types";
import { SignatureVaultSelector } from "@/components/admin/companies/legal-representatives/SignatureVaultSelector";
import { FechaCalendario } from "./FechaCalendario";
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
/** Con 8 organismos o menos se muestran como chips conmutables, sin buscador. */
const ORGANISMOS_COMO_CHIPS = 8;

const MODELOS: ReadonlyArray<{ value: SignerModel; label: string; ayuda: string; icono: ReactNode }> = [
  { value: "natural", label: "Persona natural", ayuda: "Firma con su identidad", icono: <User className="h-5 w-5" aria-hidden="true" /> },
  { value: "juridica", label: "Persona jurídica", ayuda: "La entidad, con su NIT", icono: <Building2 className="h-5 w-5" aria-hidden="true" /> },
  { value: "formato_blanco", label: "Formato en blanco", ayuda: "El PDF sin firma", icono: <FileText className="h-5 w-5" aria-hidden="true" /> },
];

const FORMAS: ReadonlyArray<{ value: SignatureMethod; label: string; ayuda: string; icono: ReactNode }> = [
  { value: "biometria", label: "Validación de identidad", ayuda: "La persona valida con un enlace", icono: <ShieldCheck className="h-5 w-5" aria-hidden="true" /> },
  { value: "baul", label: "Baúl de firmas", ayuda: "Usa una firma ya guardada", icono: <Vault className="h-5 w-5" aria-hidden="true" /> },
];

/**
 * HU #11202 / HU #13248b — alta y edición del mandatario. Un solo formulario para la compañía, el Super
 * Admin (ficha de la compañía) y el hub del organismo. Modal normal (el mismo de los demás módulos) en pasos numerados que solo
 * muestra lo que aplica: ¿Quién firma? → Datos → ¿Cómo firma? → Vigencia → Dónde aplica. El pie fijo
 * resume lo que pasará al guardar.
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
  /** z-index del panel. Por defecto `z-50`; desde otro panel hay que subir (p. ej. `z-[80]`). */
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
  const formatoBlanco = signerModel === "formato_blanco";
  // AC5: se avisa antes de guardar que cambiar de Persona natural descarta forma de firma y vigencia.
  const descartaDatos = editing != null && (editing.signerModel ?? "natural") === "natural" && !esNatural;

  const conBiometria = esNatural && signatureMethod === "biometria";
  const datosDeValidacion = {
    editing,
    metodo: signatureMethod,
    esNatural,
    tipoDocumento: documentType,
    numeroDocumento: documentNumber,
  };
  const avisoValidacion = avisoDeNuevaValidacion({ ...datosDeValidacion, correo: email });
  const consecuencia = consecuenciaDeGuardar(disparoDeValidacion(datosDeValidacion));

  const clearField = (campo: CampoMandatario) => {
    setError(null);
    setFieldErrors((prev) => {
      if (!prev[campo]) return prev;
      const next = { ...prev };
      delete next[campo];
      return next;
    });
  };

  // El bloque de fechas cambia de alto. Sin anclar el scroll, el modal se desplaza al cambiar Fija/Rango.
  const elegirVigencia = (kind: ValidityKind) => {
    const scroller = document.querySelector<HTMLElement>("[role=dialog] .overflow-y-auto");
    const top = scroller?.scrollTop ?? 0;
    setValidityKind(kind);
    clearField("validFrom");
    clearField("validTo");
    requestAnimationFrame(() => {
      if (!scroller) return;
      scroller.scrollTop = top;
      if (kind !== "range") return;
      const bloque = document.getElementById("mandatario-vigencia-fechas");
      if (!bloque) return;
      const visible = scroller.getBoundingClientRect();
      const fechas = bloque.getBoundingClientRect();
      if (fechas.bottom > visible.bottom) {
        scroller.scrollTop += fechas.bottom - visible.bottom + 8;
      }
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
  const opcionesOrganismos: OpcionSeleccionable[] = visibleOffices.map((o) => ({
    id: o.transitOfficeId,
    label: o.name,
    detalle: o.code || undefined,
    codigo: o.code || undefined,
    ariaLabel: o.name,
  }));
  const organismosElegidos: OpcionSeleccionable[] = selected.map(
    (id) => opcionesOrganismos.find((o) => o.id === id) ?? { id, label: "Organismo de tránsito" },
  );

  // Lleva la vista al primer campo con error y le da el foco (el error queda junto al campo).
  const irAlPrimerError = () => {
    setTimeout(() => {
      const campo = document.querySelector<HTMLElement>('[aria-invalid="true"]');
      if (campo) {
        campo.focus();
        return;
      }
      document.querySelector<HTMLElement>('[role="alert"]')?.scrollIntoView?.({ block: "center" });
    }, 0);
  };

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
    if (Object.keys(errores).length > 0) {
      irAlPrimerError();
      return;
    }

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
      irAlPrimerError();
    } finally {
      setSaving(false);
    }
  };

  const inputClass =
    "w-full rounded-xl border bg-white px-3 py-2 text-xs outline-none focus:border-[#557EFF] dark:bg-[#0B0F14]";

  // Pasos visibles, numerados de corrido (con Persona jurídica o Formato en blanco hay menos).
  let numero = 0;
  const siguiente = () => ++numero;

  const resumen = [
    MODELOS.find((m) => m.value === signerModel)?.label,
    esNatural && signatureMethod ? FORMAS.find((f) => f.value === signatureMethod)?.label : null,
    esNatural
      ? validityKind === "range"
        ? validFrom && validTo
          ? `Vigencia ${validFrom} a ${validTo}`
          : "Vigencia por rango"
        : "Vigencia fija"
      : null,
    `${selected.length} ${selected.length === 1 ? "organismo" : "organismos"}`,
    consecuencia,
  ].filter((x): x is string => !!x);

  return (
    <Modal
      open
      title={editing ? "Editar mandatario" : "Registrar mandatario"}
      onClose={onCancel}
      busy={saving}
      size="xl"
      zClassName={overlayClassName}
      footer={
        <div className="space-y-2">
          {error && (
            <p className="text-xs leading-tight" style={{ color: "#E5484D" }} role="alert">
              {error}
            </p>
          )}
          <p className="text-xs leading-snug text-[#59677D] dark:text-white/65" data-testid="mandatario-resumen">
            <span className="font-semibold text-[#162744] dark:text-white">Al guardar: </span>
            {resumen.join(" · ")}
          </p>
          <div className="flex flex-wrap items-center justify-end gap-2">
            <button
              type="button"
              onClick={onCancel}
              disabled={saving}
              className="rounded-full border border-[#DFE5ED] bg-white px-4 py-2 text-xs font-semibold text-[#162744] disabled:opacity-50 dark:border-white/15 dark:bg-transparent dark:text-white"
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={() => void handleSave()}
              disabled={saving}
              className="rounded-full px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
              style={{ background: "linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)" }}
            >
              {saving ? "Guardando…" : "Guardar"}
            </button>
          </div>
        </div>
      }
    >
      <div className="space-y-4" data-testid="mandatario-form">
        <Paso numero={siguiente()} titulo="¿Quién firma?" id="mandatario-paso-quien">
          <div className="grid gap-2 sm:grid-cols-3" role="radiogroup" aria-label="Modelo del mandatario">
            {MODELOS.map((m) => (
              <TarjetaOpcion
                key={m.value}
                name="mandatario-modelo"
                label={m.label}
                ayuda={m.ayuda}
                icono={m.icono}
                checked={signerModel === m.value}
                onChange={() => {
                  setSignerModel(m.value);
                  setError(null);
                  setFieldErrors({});
                }}
              />
            ))}
          </div>
          {descartaDatos && (
            <p
              className="mt-2 text-xs leading-tight"
              style={{ color: "#8a6000" }}
              role="status"
              data-testid="mandatario-cambio-modelo-aviso"
            >
              Al guardar se descartan la forma de firma y la vigencia de la Persona natural.
            </p>
          )}
        </Paso>

        <Paso numero={siguiente()} titulo="Datos" id="mandatario-paso-datos">
          {formatoBlanco ? (
            <p className="text-xs leading-tight opacity-70" data-testid="mandatario-formato-blanco-nota">
              No se piden datos: el sistema solo entrega el PDF sin firma.
            </p>
          ) : (
            <div className="space-y-3">
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

              <div className={esJuridica ? undefined : "grid gap-3 sm:grid-cols-[10rem_1fr]"}>
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

              {esNatural && (
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
                    aria-describedby={`mandatario-email-ayuda${fieldErrors.email ? " mandatario-email-error" : ""}`}
                  />
                  <FieldError id="mandatario-email-error" message={fieldErrors.email} />
                  <p id="mandatario-email-ayuda" className="mt-1 text-xs leading-tight opacity-70">
                    {conBiometria ? "Aquí le llega el enlace para validar su identidad." : "Es un dato de contacto."}
                  </p>
                </div>
              )}
            </div>
          )}
        </Paso>

        {esNatural && (
          <Paso numero={siguiente()} titulo="¿Cómo firma?" id="mandatario-paso-como">
            <div className="space-y-3">
              <div className="grid gap-2 sm:grid-cols-2" role="radiogroup" aria-label="Forma de firma">
                {FORMAS.map((f) => (
                  <TarjetaOpcion
                    key={f.value}
                    name="mandatario-forma-firma"
                    label={f.label}
                    ayuda={f.ayuda}
                    icono={f.icono}
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

              {avisoValidacion && (
                <p
                  className="text-xs leading-tight"
                  style={{ color: "#8a6000" }}
                  role="status"
                  data-testid="mandatario-aviso-validacion"
                >
                  {avisoValidacion}
                </p>
              )}

              {/* Solo al EDITAR: en el alta el mandatario aún no tiene id contra el que consultar. */}
              {editing && (
                <MandatarioIdentidadBlock
                  signer={editing}
                  onResend={onResend ? () => onResend(editing) : undefined}
                />
              )}

              {signatureMethod === "baul" && isHub && (
                <p className="text-xs leading-tight opacity-70" data-testid="mandatario-hub-firma-nota">
                  Se usa la firma que la persona tenga vigente en el baúl de la empresa.
                </p>
              )}

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
            </div>
          </Paso>
        )}

        {esNatural && (
          <Paso numero={siguiente()} titulo="Vigencia" id="mandatario-paso-vigencia">
            <div className="space-y-3">
              <div
                className="inline-flex rounded-full border border-[#DFE5ED] bg-white p-0.5 dark:bg-transparent"
                role="radiogroup"
                aria-label="Vigencia"
              >
                <Segmento
                  name="mandatario-vigencia"
                  label="Fija"
                  checked={validityKind === "fixed"}
                  onChange={() => elegirVigencia("fixed")}
                />
                <Segmento
                  name="mandatario-vigencia"
                  label="Rango de fechas"
                  checked={validityKind === "range"}
                  onChange={() => elegirVigencia("range")}
                />
              </div>
              {validityKind === "range" ? (
                <div id="mandatario-vigencia-fechas" className="grid min-w-0 gap-3 sm:grid-cols-2">
                  <div>
                    <FechaCalendario
                      id="mandatario-valid-from"
                      label="Fecha de inicio"
                      value={validFrom}
                      invalid={!!fieldErrors.validFrom}
                      describedBy={fieldErrors.validFrom ? "mandatario-valid-from-error" : undefined}
                      onChange={(iso) => {
                        setValidFrom(iso);
                        clearField("validFrom");
                        clearField("validTo");
                      }}
                    />
                    <FieldError id="mandatario-valid-from-error" message={fieldErrors.validFrom} />
                  </div>
                  <div>
                    <FechaCalendario
                      id="mandatario-valid-to"
                      label="Fecha de fin"
                      value={validTo}
                      invalid={!!fieldErrors.validTo}
                      describedBy={fieldErrors.validTo ? "mandatario-valid-to-error" : undefined}
                      onChange={(iso) => {
                        setValidTo(iso);
                        clearField("validTo");
                      }}
                    />
                    <FieldError id="mandatario-valid-to-error" message={fieldErrors.validTo} />
                  </div>
                </div>
              ) : (
                <p className="text-xs leading-tight opacity-70">Vale mientras el mandatario esté activo.</p>
              )}
              <FieldError id="mandatario-vigencia-error" message={fieldErrors.validityKind} />
            </div>
          </Paso>
        )}

        <Paso numero={siguiente()} titulo="Dónde aplica" id="mandatario-paso-donde">
          <div className="space-y-4">
            <fieldset>
              <legend className="mb-1.5 block text-xs font-semibold">Organismos de tránsito</legend>
              {isHub ? (
                <p className="text-xs font-semibold text-[#162744] dark:text-white" data-testid="mandatario-organismo-fijo">
                  {visibleOffices[0]?.name ?? "Este organismo"}
                  {visibleOffices[0]?.code ? (
                    <span className="ml-1 font-normal text-[#59677D]">· {visibleOffices[0].code}</span>
                  ) : null}
                </p>
              ) : (
                <MultiSelectBuscable
                  testId="mandatario-organismos"
                  grupo="Organismos de tránsito"
                  genero="m"
                  opciones={opcionesOrganismos}
                  seleccion={organismosElegidos}
                  onChange={(next) => {
                    clearField("offices");
                    setSelected(next.map((o) => o.id));
                  }}
                  buscarLabel="Buscar organismo por nombre o código"
                  buscarPlaceholder="Buscar por nombre o código…"
                  chipsHasta={ORGANISMOS_COMO_CHIPS}
                  textoVacio="No hay organismos habilitados."
                />
              )}
              <p className="mt-1.5 text-xs leading-tight opacity-70">
                {isHub
                  ? "El mandatario se registra en este organismo."
                  : "Solo los organismos habilitados para esta compañía. Quitar uno retira al mandatario de ese organismo; en los demás sigue."}
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
                  ayuda={
                    isHub && ownerCompanyIds.length === 0
                      ? "Opcional. Si no eliges ninguna, el mandatario queda en el organismo y puedes asignarlo después a una compañía."
                      : undefined
                  }
                />
              </fieldset>
            ) : null}
          </div>
        </Paso>
      </div>
    </Modal>
  );
}

/** Paso numerado del panel: tarjeta blanca con el número en círculo de marca y un título corto. */
function Paso({
  numero,
  titulo,
  id,
  children,
}: {
  numero: number;
  titulo: string;
  id: string;
  children: ReactNode;
}) {
  return (
    <section
      aria-labelledby={`${id}-titulo`}
      className="rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/15 dark:bg-[#162744]"
    >
      <h3 id={`${id}-titulo`} className="mb-3 flex items-center gap-2 text-sm font-bold text-[#162744] dark:text-white">
        <span
          aria-hidden="true"
          className="grid h-6 w-6 shrink-0 place-items-center rounded-full text-xs font-bold text-white"
          style={{ background: "#557EFF" }}
        >
          {numero}
        </span>
        {titulo}
      </h3>
      {children}
    </section>
  );
}

/** Tarjeta con ícono y una línea de ayuda; el radio nativo queda oculto pero operable con teclado. */
function TarjetaOpcion({
  name,
  label,
  ayuda,
  icono,
  checked,
  onChange,
}: {
  name: string;
  label: string;
  ayuda: string;
  icono: ReactNode;
  checked: boolean;
  onChange: () => void;
}) {
  return (
    <label
      className="flex cursor-pointer items-start gap-2.5 rounded-xl border px-3 py-2.5 transition-colors hover:bg-[rgba(85,126,255,0.04)] has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-[#557EFF] has-[:focus-visible]:ring-offset-2"
      style={
        checked
          ? { borderColor: "#8CC63F", background: "rgba(140,198,63,0.12)" }
          : { borderColor: "#DFE5ED", background: "#FFFFFF" }
      }
    >
      <input
        type="radio"
        name={name}
        checked={checked}
        onChange={onChange}
        aria-label={label}
        className="sr-only"
      />
      <span className="mt-0.5 shrink-0 text-[#557EFF]">{icono}</span>
      <span className="min-w-0">
        <span className="block text-xs font-semibold leading-snug text-[#162744]">{label}</span>
        <span className="mt-0.5 block text-xs text-[#59677D]">{ayuda}</span>
      </span>
    </label>
  );
}

/** Opción de un control segmentado (Fija | Rango de fechas). */
function Segmento({
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
      className="relative cursor-pointer rounded-full px-4 py-1.5 text-xs font-semibold transition-colors has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-[#557EFF]"
      style={checked ? { background: "#557EFF", color: "#FFFFFF" } : { color: "#59677D" }}
      // El radio está oculto con position:absolute. Si toma el foco, el modal (overlay con
      // desenfoque) lo lleva al inicio del scroll. El clic sigue cambiando la opción.
      onMouseDown={(e) => e.preventDefault()}
    >
      <input
        type="radio"
        name={name}
        checked={checked}
        onChange={onChange}
        aria-label={label}
        className="sr-only"
      />
      {label}
    </label>
  );
}

function FieldError({ id, message }: { id: string; message?: string }) {
  if (!message) return null;
  return (
    <p id={id} className="mt-1 text-xs leading-tight" style={{ color: "#E5484D" }} role="alert">
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
