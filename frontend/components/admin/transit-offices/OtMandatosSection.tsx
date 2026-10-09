"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Eye, FileText, Pencil, RotateCcw, Send, Trash2, UserX } from "lucide-react";
import { MandatarioGeneralCard } from "@/components/admin/transit-offices/MandatarioGeneralCard";
import { SectionTabs } from "@/components/atom/SectionTabs";
import { CompanyMandatarioForm } from "@/components/admin/companies/mandate-signers/CompanyMandatarioForm";
import { MandatoOtConfigForm, type MandatoOtConfigPanelMode } from "@/components/admin/plataforma/MandatoOtConfigForm";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { OtCompaniasMandatarioTable } from "@/components/admin/transit-offices/OtCompaniasMandatarioTable";
import { RowActionsMenu } from "@/components/atom/RowActionsMenu";
import type { RowAction } from "@/components/atom/RowActions";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { MandatarioFirmaPreviewDialog } from "@/components/admin/transit-offices/MandatarioFirmaPreviewDialog";
import {
  fetchMandateOtConfig,
  listCompanyOtMandateRules,
  listOtMandatoFormats,
  type CompanyOtMandateRuleView,
  type MandateOtConfigView,
  type MandatoFormatView,
} from "@/lib/api/admin-plataforma-mandatos";
import {
  createMandateSigner,
  deleteMandateSigner,
  fetchMandateSignerImpact,
  fetchMandateSigners,
  inactivateMandateSigner,
  reactivateMandateSigner,
  reconcileMandateSignerIdentity,
  resendMandateSignerIdentity,
  updateMandateSigner,
  type CompanyMandateSignerInput,
  type AssociableCompany,
  type MandateSigner,
} from "@/lib/api/admin-mandate-signers";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload, isOtAdmin, isSuperAdmin } from "@/lib/auth/jwt";
import { ApiError } from "@/lib/api/types";
import {
  etiquetaTipoFirma,
  tipoDeFirmaMandatario,
} from "@/lib/plataforma/mandatario-firma";
import { etiquetaModelo, modeloDe } from "@/lib/plataforma/mandatario-vigencia";
import { MandatarioCandado } from "@/components/admin/companies/mandate-signers/MandatarioCandado";
import { MandatarioBajaDialog } from "@/components/admin/companies/mandate-signers/MandatarioBajaDialog";
import { puedeEditarMandatario, puedeEliminarMandatario } from "@/lib/plataforma/mandatario-permisos";
import {
  mensajeErrorAccion,
  mensajeResultadoBaja,
  mensajeResultadoReactivar,
  type AccionBaja,
} from "@/lib/plataforma/mandatario-baja";
import {
  mensajeErrorReenvio,
  mensajeReenvio,
  mensajeValidacionTrasGuardar,
  presentarValidacion,
  puedeReenviarValidacion,
  requiereValidacionPropia,
  sincronizarValidacionesEnCurso,
} from "@/lib/plataforma/mandatario-validacion";
import { useSincronizarValidacionesEnCurso } from "@/lib/plataforma/useSincronizarValidacionesEnCurso";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { MandatarioVigenciaBadge } from "@/components/admin/companies/mandate-signers/MandatarioVigenciaBadge";
import { rlPrimaryCtaClass, rlPrimaryCtaStyle } from "@/components/admin/companies/legal-representatives/rl-flit-styles";
import { mandatoFormatName } from "@/lib/plataforma/mandato-templates";


/** Formato de contrato del organismo tal como se muestra: nombre del catálogo o, si no cargó, el código. */
export interface OtFormatoVista {
  nombre: string;
  /** El catálogo no cargó: `nombre` es el código guardado. */
  fallo: boolean;
}

/** Tarjeta compacta «Formato de contrato: {nombre}». Vive en el encabezado del hub (`titleRight`). */
export function OtFormatoContratoChip({ formato }: { formato: OtFormatoVista | null }) {
  if (!formato) return null;
  return (
    <div className="flex flex-col items-start gap-1 md:items-end">
      <p
        className="inline-flex items-center gap-2 rounded-full border border-[#D6E2FF] bg-[#EFF4FF] px-3 py-1.5 text-xs text-[#59677D] dark:border-white/15 dark:bg-white/5 dark:text-white/70"
        data-testid="ot-formato-contrato"
      >
        <FileText className="h-3.5 w-3.5 shrink-0 text-[#557EFF]" aria-hidden />
        <span>
          Formato de contrato:{" "}
          <span className="font-semibold text-[#162244] dark:text-white">{formato.nombre}</span>
        </span>
      </p>
      {formato.fallo ? (
        <span className="text-[11px] text-[#59677D] dark:text-white/60">
          No se pudo cargar el nombre del catálogo
        </span>
      ) : null}
    </div>
  );
}

export function OtMandatosSection({
  transitOfficeId,
  onFormatoChange,
}: {
  transitOfficeId: string;
  /**
   * Si se pasa, la sección NO pinta la tarjeta del formato: se la avisa a quien la pase para que la
   * ponga en el encabezado de la página (`OtFormatoContratoChip`). Sin él la pinta arriba de las pestañas.
   */
  onFormatoChange?: (formato: OtFormatoVista | null) => void;
}) {
  const { show } = useToast();
  const [status, setStatus] = useState<"loading" | "ready" | "error">("loading");
  const [error, setError] = useState<string | null>(null);
  const [office, setOffice] = useState<MandateOtConfigView | null>(null);
  const [formatos, setFormatos] = useState<readonly MandatoFormatView[]>([]);
  const [formatosError, setFormatosError] = useState(false);
  const [companies, setCompanies] = useState<CompanyOtMandateRuleView[]>([]);
  const [signers, setSigners] = useState<MandateSigner[]>([]);
  const [previewSigner, setPreviewSigner] = useState<MandateSigner | null>(null);
  // Bug #13055 — tablas homologadas con el modelo de trámites: «Filas por página». El mandatario
  // general es una sola fila fija y no pagina; las compañías paginan en servidor (HU #13182).
  const pgSigners = usePaginacion();
  const [seccion, setSeccion] = useState<"companias" | "mandatarios">("mandatarios");
  const [panel, setPanel] = useState<{
    mode: MandatoOtConfigPanelMode;
    companyId: string | null;
    company?: AssociableCompany | null;
  } | null>(null);
  const [signerEpoch, setSignerEpoch] = useState(0);
  const [lastCreatedSignerId, setLastCreatedSignerId] = useState<string | null>(null);
  // HU #13124 — el alta se hace contra la ruta del OT: solo ot_admin y SuperAdmin pueden escribir
  // (HU #13123). Al Operador OT (gestor_tramites_ot) no se le ofrece registrar mandatarios.
  const [canRegisterSigner] = useState(() => {
    const payload = decodeJwtPayload(getToken());
    return isSuperAdmin(payload) || isOtAdmin(payload);
  });
  // Alta desde el panel de configuración: con compañía, el mandatario de esa empresa; con `null`, el del propio OT.
  const [registro, setRegistro] = useState<{ companyId: string | null } | null>(null);
  // Alta directa desde la pestaña Mandatarios: el organismo queda fijo y las compañías son opcionales.
  const [creating, setCreating] = useState(false);
  // HU #13139 — edición del mandatario desde la lista (solo si el servidor lo permite por rol y origen).
  const [editingSigner, setEditingSigner] = useState<MandateSigner | null>(null);
  // HU #13140 — confirmación previa de desactivar o eliminar; reactivar es directo.
  const [baja, setBaja] = useState<{ signer: MandateSigner; accion: AccionBaja } | null>(null);
  const [busySignerId, setBusySignerId] = useState<string | null>(null);

  const load = useCallback(async (opts?: { silent?: boolean }) => {
    if (!opts?.silent) {
      setStatus("loading");
      setError(null);
    }
    try {
      const [view, rules, signerList, catalogo] = await Promise.all([
        fetchMandateOtConfig(transitOfficeId),
        listCompanyOtMandateRules(transitOfficeId),
        fetchMandateSigners(transitOfficeId),
        listOtMandatoFormats(transitOfficeId).then(
          (items) => ({ items, error: false }),
          () => ({ items: [] as MandatoFormatView[], error: true }),
        ),
      ]);
      setOffice(view);
      setCompanies(rules);
      setSigners(signerList);
      setFormatos(catalogo.items);
      setFormatosError(catalogo.error);
      setStatus("ready");
      // Como la pantalla de espera del trámite: si alguna validación en curso ya se resolvió y el aviso
      // del proveedor no llegó, la consulta la actualiza y se recarga la lista.
      if (canRegisterSigner) {
        const cambio = await sincronizarValidacionesEnCurso(signerList, (sg) =>
          reconcileMandateSignerIdentity(transitOfficeId, sg.id),
        );
        if (cambio) setSigners(await fetchMandateSigners(transitOfficeId));
      }
    } catch (err) {
      setOffice(null);
      setCompanies([]);
      setSigners([]);
      setStatus("error");
      setError(err instanceof ApiError ? err.message : "No se pudo cargar la configuración de mandatos.");
    }
  }, [transitOfficeId, canRegisterSigner]);

  useEffect(() => {
    // Carga inicial: status loading/ready vive en este módulo.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- fetch al montar / cambiar id
    void load();
  }, [load]);

  // Mientras la lista está abierta, las validaciones en curso se siguen consultando solas.
  useSincronizarValidacionesEnCurso(
    signers,
    (sg) => reconcileMandateSignerIdentity(transitOfficeId, sg.id),
    () => void load({ silent: true }),
    canRegisterSigner,
  );

  const formatoVista = useMemo<OtFormatoVista | null>(
    () =>
      office
        ? {
            nombre: formatosError
              ? office.templateCode
              : mandatoFormatName(formatos, office.templateCode) || office.templateCode,
            fallo: formatosError,
          }
        : null,
    [office, formatos, formatosError],
  );
  useEffect(() => {
    onFormatoChange?.(formatoVista);
  }, [onFormatoChange, formatoVista]);

  // Mandatario por defecto ya configurado por compañía (reglas del OT); las compañías sin regla
  // (p. ej. que nunca radicaron) quedan «Sin definir».
  const signerNameByCompany = useMemo(
    () => new Map(companies.map((r) => [r.companyTenantId, r.defaultMandateSignerName])),
    [companies],
  );
  const signerNameOf = useCallback(
    (companyTenantId: string) => signerNameByCompany.get(companyTenantId),
    [signerNameByCompany],
  );
  // HU #13139 — «Sin mandatario»: sin default propio, sin mandatario activo vinculado y sin general del organismo.
  const sinMandatarioOf = useCallback(
    (companyTenantId: string) =>
      !signerNameByCompany.get(companyTenantId)?.trim() &&
      !office?.defaultMandateSignerName?.trim() &&
      !signers.some((sg) => sg.isActive && sg.companyTenantIds.includes(companyTenantId)),
    [signerNameByCompany, office?.defaultMandateSignerName, signers],
  );
  const editCompany = useCallback(
    (company: AssociableCompany) =>
      setPanel({ mode: "mandatario", companyId: company.id, company }),
    [],
  );

  if (status === "loading") {
    return <CarLoaderModal label="Cargando mandatos…" />;
  }

  if (status === "error" || !office) {
    return (
      <UiStateBoundary
        status="error"
        errorMessage={error ?? "No se pudo cargar la configuración de mandatos."}
        onRetry={() => void load()}
      />
    );
  }

  const reactivar = async (signer: MandateSigner) => {
    setBusySignerId(signer.id);
    try {
      const result = await reactivateMandateSigner(transitOfficeId, signer.id);
      show(mensajeResultadoReactivar(signer.fullName, result), "success");
      await load({ silent: true });
    } catch (err) {
      show(mensajeErrorAccion(err), "error");
    } finally {
      setBusySignerId(null);
    }
  };

  // HU #13248 — «Reenviar validación» desde la ficha y desde la fila.
  const reenviarValidacion = (signer: MandateSigner) =>
    resendMandateSignerIdentity(transitOfficeId, signer.id);

  const reenviarDesdeFila = async (signer: MandateSigner) => {
    setBusySignerId(signer.id);
    try {
      const result = await reenviarValidacion(signer);
      show(mensajeReenvio(result, signer.email), "success");
      await load({ silent: true });
    } catch (err) {
      show(mensajeErrorReenvio(err), "error");
    } finally {
      setBusySignerId(null);
    }
  };

  const ejecutarBaja = async (signer: MandateSigner, accion: AccionBaja, confirmarImpacto: boolean) => {
    const outcome =
      accion === "eliminar"
        ? await deleteMandateSigner(transitOfficeId, signer.id, confirmarImpacto)
        : await inactivateMandateSigner(transitOfficeId, signer.id);
    show(mensajeResultadoBaja(signer.fullName, accion, outcome), "success");
    await load({ silent: true });
  };

  // El mandatario general se muestra en su tarjeta; la tabla lista a los demás.
  const generalSigner = signers.find((sg) => sg.id === office.defaultMandateSignerId) ?? null;
  const otrosSigners = generalSigner ? signers.filter((sg) => sg.id !== generalSigner.id) : signers;

  const companyNameById = new Map(companies.map((c) => [c.companyTenantId, c.companyName]));

  // Acciones sobre una persona (tabla y tarjeta del mandatario general).
  const accionesDe = (row: MandateSigner): RowAction[] => [
          ...(canRegisterSigner && puedeEditarMandatario(row)
            ? [
                {
                  icon: Pencil,
                  label: `Editar mandatario ${row.fullName}`,
                  tone: "primary" as const,
                  onClick: () => setEditingSigner(row),
                },
              ]
            : []),
          ...(canRegisterSigner && puedeEditarMandatario(row)
            ? [
                row.isActive
                  ? {
                      icon: UserX,
                      label: `Desactivar mandatario ${row.fullName}`,
                      tone: "danger" as const,
                      disabled: busySignerId === row.id,
                      onClick: () => setBaja({ signer: row, accion: "desactivar" }),
                    }
                  : {
                      icon: RotateCcw,
                      label: `Reactivar mandatario ${row.fullName}`,
                      disabled: busySignerId === row.id,
                      onClick: () => void reactivar(row),
                    },
              ]
            : []),
          ...(canRegisterSigner && puedeEditarMandatario(row) && puedeReenviarValidacion(row)
            ? [
                {
                  icon: Send,
                  label: `Reenviar validación a ${row.fullName}`,
                  disabled: busySignerId === row.id,
                  onClick: () => void reenviarDesdeFila(row),
                },
              ]
            : []),
          ...(canRegisterSigner && puedeEliminarMandatario(row)
            ? [
                {
                  icon: Trash2,
                  label: `Eliminar mandatario ${row.fullName}`,
                  tone: "danger" as const,
                  disabled: busySignerId === row.id,
                  onClick: () => setBaja({ signer: row, accion: "eliminar" }),
                },
              ]
            : []),
          ...(modeloDe(row) === "natural"
            ? [
                {
                  icon: Eye,
                  label: `Ver firma de ${row.fullName}`,
                  tone: "primary" as const,
                  onClick: () => setPreviewSigner(row),
                },
              ]
            : []),
        ];

  const signerColumns: DataTableColumn<MandateSigner>[] = [
    {
      key: "name",
      header: "Nombre",
      cellClassName: "font-semibold",
      render: (row) => (
        <>
          {row.fullName}
          {/* El Admin OT ve sus acciones; el candado solo aplica a quien no puede tocarlo. */}
          {row.origin === "organismo" && !puedeEditarMandatario(row) ? <MandatarioCandado /> : null}
        </>
      ),
    },
    {
      key: "docType",
      header: "Tipo documento",
      render: (row) => dash(row.documentType),
    },
    {
      key: "docNumber",
      header: "Documento",
      cellClassName: "font-mono",
      render: (row) => dash(row.documentNumber),
    },
    {
      key: "modelo",
      header: "Modelo",
      render: (row) => etiquetaModelo(row),
    },
    {
      key: "firma",
      header: "Tipo de firma",
      // Persona jurídica y Formato en blanco no firman con medio propio.
      render: (row) =>
        modeloDe(row) === "natural" ? etiquetaTipoFirma(tipoDeFirmaMandatario(row)) : "—",
    },
    {
      key: "vigencia",
      header: "Vigencia",
      render: (row) => <MandatarioVigenciaBadge signer={row} />,
    },
    {
      key: "validacion",
      header: "Validación",
      render: (row) =>
        requiereValidacionPropia(row) ? (
          <StatusBadge
            tone={presentarValidacion(row.identityStatus).tone}
            label={presentarValidacion(row.identityStatus).texto}
            ariaLabel={`Validación: ${presentarValidacion(row.identityStatus).texto}`}
          />
        ) : (
          "—"
        ),
    },
    {
      key: "actions",
      header: "Acción",
      align: "right",
      render: (row) => {
        const actions = accionesDe(row);
        return actions.length > 0 ? (
          <RowActionsMenu ariaLabel={`Acciones de ${row.fullName}`} subject={row.fullName} actions={actions} />
        ) : null;
      },
    },
  ];

  return (
    <div className="flex flex-col gap-4" data-testid="ot-mandatos-section">
      {!onFormatoChange && <OtFormatoContratoChip formato={formatoVista} />}
      <SectionTabs
        ariaLabel="Secciones de mandatos del organismo"
        active={seccion}
        onChange={setSeccion}
        tabs={[
          {
            id: "mandatarios",
            label: "Mandatarios",
            count: signers.length,
            content: (
      <div className="flex flex-col gap-6">
      {canRegisterSigner ? (
        <div className="flex justify-end">
          <button
            type="button"
            className={rlPrimaryCtaClass}
            style={rlPrimaryCtaStyle}
            data-testid="ot-nuevo-mandatario"
            onClick={() => setCreating(true)}
          >
            Nuevo mandatario
          </button>
        </div>
      ) : null}
      <MandatarioGeneralCard
        nombre={office.defaultMandateSignerName}
        tipoDocumento={office.defaultMandateSignerDocumentType}
        numeroDocumento={office.defaultMandateSignerDocumentNumber}
        signer={generalSigner}
        puedeEditar
        acciones={generalSigner ? accionesDe(generalSigner) : []}
        onEditar={() => setPanel({ mode: "mandatario", companyId: null })}
      />
      <div className="flex flex-col gap-3" data-testid="ot-mandatos-signers-card">
        <div>
          <h3 className="text-sm font-semibold text-[#162244] dark:text-white">Otros mandatarios del organismo</h3>
          <p className="mt-1 text-xs leading-relaxed text-[#59677D] dark:text-white/65">
            Las demás personas registradas en este organismo, se usen o no por empresa.
          </p>
        </div>
        <DataTable
          columns={signerColumns}
          rows={pgSigners.paginar(otrosSigners)}
          getRowKey={(row) => row.id}
          ariaLabel="Mandatarios del organismo"
          minWidth={720}
          emptyMessage="No hay mandatarios creados en este organismo."
          pagination={{
            page: pgSigners.page,
            pageSize: pgSigners.pageSize,
            totalCount: otrosSigners.length,
            onPageChange: pgSigners.setPage,
            onPageSizeChange: pgSigners.setPageSize,
          }}
        />
      </div>
      </div>
            ),
          },
          {
            id: "companias",
            label: "Compañías",
            content: (
              <div className="flex flex-col gap-6">
      <OtCompaniasMandatarioTable
        transitOfficeId={transitOfficeId}
        signerNameOf={signerNameOf}
        sinMandatarioOf={sinMandatarioOf}
        onEdit={editCompany}
      />
              </div>
            ),
          },
        ]}
      />

      {panel && office ? (
        <MandatoOtConfigForm
          key={`${panel.mode}-${panel.companyId ?? "ot"}`}
          office={office}
          mode={panel.mode}
          highlightCompanyId={panel.companyId}
          lockToCompanyId={panel.companyId}
          lockedCompany={panel.company ?? null}
          signersRevision={signerEpoch}
          lastCreatedSignerId={lastCreatedSignerId}
          onRegisterSigner={
            canRegisterSigner ? (companyId) => setRegistro({ companyId: companyId ?? null }) : undefined
          }
          onClose={() => {
            setPanel(null);
            void load({ silent: true });
          }}
          onSaved={(view) => {
            setOffice(view);
            setPanel(null);
            void load();
          }}
        />
      ) : null}

      {creating ? (
        <CompanyMandatarioForm
          variant="hub"
          offices={[{ transitOfficeId, code: office.code, name: office.name }]}
          editing={null}
          initialOfficeIds={[transitOfficeId]}
          restrictToOfficeIds={[transitOfficeId]}
          overlayClassName="z-[80]"
          onCancel={() => setCreating(false)}
          onSubmit={async (input: CompanyMandateSignerInput) => {
            const saved = await createMandateSigner(transitOfficeId, {
              fullName: input.fullName,
              documentType: input.documentType,
              documentNumber: input.documentNumber,
              email: input.email,
              companyTenantIds: [],
              transitOfficeIds: [transitOfficeId],
              // Firma del baúl del propio OT, elegida o capturada en el formulario.
              signatureVaultId: input.signatureVaultId,
              signerModel: input.signerModel,
              signatureMethod: input.signatureMethod,
              validityKind: input.validityKind,
              validFrom: input.validFrom,
              validTo: input.validTo,
              officeCompanies: input.officeCompanies,
            });
            setCreating(false);
            const validacion = mensajeValidacionTrasGuardar(saved, input.email);
            show(
              validacion ? `Mandatario registrado. ${validacion}` : "Mandatario registrado.",
              saved.identity === "failed" ? "error" : "success",
            );
            void load();
            return saved;
          }}
        />
      ) : null}

      {registro ? (
        <CompanyMandatarioForm
          variant="hub"
          offices={[{ transitOfficeId, code: office.code, name: office.name }]}
          editing={null}
          initialOfficeIds={[transitOfficeId]}
          restrictToOfficeIds={[transitOfficeId]}
          ownerCompanyIds={registro.companyId ? [registro.companyId] : []}
          overlayClassName="z-[80]"
          onCancel={() => setRegistro(null)}
          onSubmit={async (input: CompanyMandateSignerInput) => {
            const saved = await createMandateSigner(transitOfficeId, {
              fullName: input.fullName,
              documentType: input.documentType,
              documentNumber: input.documentNumber,
              email: input.email,
              companyTenantIds: registro.companyId ? [registro.companyId] : [],
              transitOfficeIds: [transitOfficeId],
              // Mandatario del OT: firma del baúl del propio OT. El de una compañía la resuelve el servidor en su baúl.
              signatureVaultId: registro.companyId ? undefined : input.signatureVaultId,
              // HU #13132 — modelo, forma de firma y vigencia.
              signerModel: input.signerModel,
              signatureMethod: input.signatureMethod,
              validityKind: input.validityKind,
              validFrom: input.validFrom,
              validTo: input.validTo,
              // HU #13181 — compañías asociadas elegidas en el formulario.
              officeCompanies: input.officeCompanies,
            });
            setRegistro(null);
            setLastCreatedSignerId(saved.id);
            setSignerEpoch((n) => n + 1);
            const base =
              panel?.mode === "mandatario" && !panel.companyId
                ? "Mandatario registrado. Quedó preseleccionado como general del OT; guarda el firmante para fijarlo."
                : "Mandatario registrado. Ya puedes asociarlo como default de la empresa.";
            const validacion = mensajeValidacionTrasGuardar(saved, input.email);
            show(validacion ? `${base} ${validacion}` : base, saved.identity === "failed" ? "error" : "success");
            void load();
            return saved;
          }}
        />
      ) : null}

      {editingSigner ? (
        <CompanyMandatarioForm
          variant="hub"
          offices={[{ transitOfficeId, code: office.code, name: office.name }]}
          editing={editingSigner}
          restrictToOfficeIds={[transitOfficeId]}
          // HU #13181c — la propia compañía del mandatario no se ofrece como asociada (igual que en el alta).
          ownerCompanyIds={editingSigner.companyTenantIds}
          overlayClassName="z-[80]"
          onCancel={() => setEditingSigner(null)}
          onResend={reenviarValidacion}
          onSubmit={async (input: CompanyMandateSignerInput) => {
            const saved = await updateMandateSigner(transitOfficeId, editingSigner.id, {
              fullName: input.fullName,
              documentType: input.documentType,
              documentNumber: input.documentNumber,
              email: input.email,
              companyTenantIds: editingSigner.companyTenantIds,
              transitOfficeIds: [transitOfficeId],
              // Solo el mandatario del propio OT gestiona aquí su firma del baúl (el formulario no la manda para otros).
              signatureVaultId: input.signatureVaultId ?? undefined,
              signerModel: input.signerModel,
              signatureMethod: input.signatureMethod,
              validityKind: input.validityKind,
              validFrom: input.validFrom,
              validTo: input.validTo,
              // HU #13181c — las compañías asociadas viajan también al editar (antes se descartaban).
              officeCompanies: input.officeCompanies,
            });
            setEditingSigner(null);
            const validacion = mensajeValidacionTrasGuardar(saved, input.email);
            show(
              validacion ? `Mandatario actualizado. ${validacion}` : "Mandatario actualizado.",
              saved.identity === "failed" ? "error" : "success",
            );
            void load();
            return saved;
          }}
        />
      ) : null}

      {baja ? (
        <MandatarioBajaDialog
          signer={baja.signer}
          accion={baja.accion}
          loadImpact={(signal) => fetchMandateSignerImpact(transitOfficeId, baja.signer.id, signal)}
          onConfirm={(confirmar) => ejecutarBaja(baja.signer, baja.accion, confirmar)}
          onClose={() => setBaja(null)}
          officeLabel={(id) => (id === transitOfficeId ? office.name : "otro organismo")}
          companyLabel={(id) => companyNameById.get(id) ?? "Otra compañía"}
        />
      ) : null}

      {previewSigner ? (
        <MandatarioFirmaPreviewDialog
          signer={previewSigner}
          officeId={transitOfficeId}
          onClose={() => setPreviewSigner(null)}
        />
      ) : null}
    </div>
  );
}

function dash(value: string | null | undefined): string {
  const text = value?.trim();
  return text ? text : "—";
}
