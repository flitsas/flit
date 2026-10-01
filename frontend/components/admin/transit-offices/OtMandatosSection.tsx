"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Eye, Pencil, RotateCcw, Trash2, UserX } from "lucide-react";
import { CompanyMandatarioForm } from "@/components/admin/companies/mandate-signers/CompanyMandatarioForm";
import { MandatoOtConfigForm, type MandatoOtConfigPanelMode } from "@/components/admin/plataforma/MandatoOtConfigForm";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { OtCompaniasMandatarioTable } from "@/components/admin/transit-offices/OtCompaniasMandatarioTable";
import { RowActions } from "@/components/atom/RowActions";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { MandatarioFirmaPreviewDialog } from "@/components/admin/transit-offices/MandatarioFirmaPreviewDialog";
import {
  fetchMandateOtConfig,
  listCompanyOtMandateRules,
  type CompanyOtMandateRuleView,
  type MandateOtConfigView,
} from "@/lib/api/admin-plataforma-mandatos";
import {
  createMandateSigner,
  deleteMandateSigner,
  fetchMandateSignerImpact,
  fetchMandateSigners,
  inactivateMandateSigner,
  reactivateMandateSigner,
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
import { MandatarioVigenciaBadge } from "@/components/admin/companies/mandate-signers/MandatarioVigenciaBadge";


export function OtMandatosSection({ transitOfficeId }: { transitOfficeId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<"loading" | "ready" | "error">("loading");
  const [error, setError] = useState<string | null>(null);
  const [office, setOffice] = useState<MandateOtConfigView | null>(null);
  const [companies, setCompanies] = useState<CompanyOtMandateRuleView[]>([]);
  const [signers, setSigners] = useState<MandateSigner[]>([]);
  const [previewSigner, setPreviewSigner] = useState<MandateSigner | null>(null);
  // Bug #13055 — tablas homologadas con el modelo de trámites: «Filas por página». El mandatario
  // general es una sola fila fija y no pagina; las compañías paginan en servidor (HU #13182).
  const pgSigners = usePaginacion();
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
  const [signerCompanyId, setSignerCompanyId] = useState<string | null>(null);
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
      const [view, rules, signerList] = await Promise.all([
        fetchMandateOtConfig(transitOfficeId),
        listCompanyOtMandateRules(transitOfficeId),
        fetchMandateSigners(transitOfficeId),
      ]);
      setOffice(view);
      setCompanies(rules);
      setSigners(signerList);
      setStatus("ready");
    } catch (err) {
      setOffice(null);
      setCompanies([]);
      setSigners([]);
      setStatus("error");
      setError(err instanceof ApiError ? err.message : "No se pudo cargar la configuración de mandatos.");
    }
  }, [transitOfficeId]);

  useEffect(() => {
    // Carga inicial: status loading/ready vive en este módulo.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- fetch al montar / cambiar id
    void load();
  }, [load]);

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

  const generalRow: GeneralMandatarioRow = {
    id: office.officeId,
    nit: null,
    name: office.name,
    signerName: office.defaultMandateSignerName,
    docType: office.defaultMandateSignerDocumentType,
    docNumber: office.defaultMandateSignerDocumentNumber,
    hash: office.defaultMandateSignerIntegrityHash,
  };

  const generalColumns: DataTableColumn<GeneralMandatarioRow>[] = [
    {
      key: "nit",
      header: "NIT",
      cellClassName: "font-mono",
      render: (row) => dash(row.nit),
    },
    {
      key: "name",
      header: "Organismo",
      cellClassName: "font-semibold",
      render: (row) => row.name,
    },
    {
      key: "signer",
      header: "Mandatario",
      render: (row) => (
        <span data-testid="ot-mandatos-general-signer">{signerCell(row.signerName)}</span>
      ),
    },
    {
      key: "actions",
      header: "Acción",
      align: "right",
      render: () => (
        <RowActions
          actions={[
            {
              icon: Pencil,
              label: "Editar mandatario general del organismo",
              tone: "primary",
              onClick: () => setPanel({ mode: "mandatario", companyId: null }),
            },
          ]}
        />
      ),
    },
  ];

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

  const ejecutarBaja = async (signer: MandateSigner, accion: AccionBaja, confirmarImpacto: boolean) => {
    const outcome =
      accion === "eliminar"
        ? await deleteMandateSigner(transitOfficeId, signer.id, confirmarImpacto)
        : await inactivateMandateSigner(transitOfficeId, signer.id);
    show(mensajeResultadoBaja(signer.fullName, accion, outcome), "success");
    await load({ silent: true });
  };

  const companyNameById = new Map(companies.map((c) => [c.companyTenantId, c.companyName]));

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
      key: "actions",
      header: "Acción",
      align: "right",
      render: (row) => {
        const actions = [
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
        return actions.length > 0 ? <RowActions actions={actions} /> : null;
      },
    },
  ];

  return (
    <div className="flex flex-col gap-4" data-testid="ot-mandatos-section">
      <div className="flex flex-col gap-3" data-testid="ot-mandatos-general-card">
        <div>
          <h2 className="text-sm font-semibold text-[#162244] dark:text-white">
            Mandatario general del organismo
          </h2>
          <p className="mt-1 text-xs leading-relaxed text-[#59677D] dark:text-white/65">
            Persona natural por defecto para los trámites de este OT. Se usa cuando la empresa que
            radica no tiene mandatario propio. Si hay default cliente×OT, ese prima.
          </p>
        </div>
        <DataTable
          columns={generalColumns}
          rows={[generalRow]}
          getRowKey={(row) => row.id}
          ariaLabel="Mandatario general del organismo"
          minWidth={720}
        />
      </div>

      <OtCompaniasMandatarioTable
        transitOfficeId={transitOfficeId}
        signerNameOf={signerNameOf}
        sinMandatarioOf={sinMandatarioOf}
        onEdit={editCompany}
      />

      <div className="flex flex-col gap-3" data-testid="ot-mandatos-signers-card">
        <div>
          <h3 className="text-sm font-semibold text-[#162244] dark:text-white">Mandatarios</h3>
          <p className="mt-1 text-xs leading-relaxed text-[#59677D] dark:text-white/65">
            Personas registradas en este organismo, se usen o no como general o por empresa.
          </p>
        </div>
        <DataTable
          columns={signerColumns}
          rows={pgSigners.paginar(signers)}
          getRowKey={(row) => row.id}
          ariaLabel="Mandatarios del organismo"
          minWidth={720}
          emptyMessage="No hay mandatarios creados en este organismo."
          pagination={{
            page: pgSigners.page,
            pageSize: pgSigners.pageSize,
            totalCount: signers.length,
            onPageChange: pgSigners.setPage,
            onPageSizeChange: pgSigners.setPageSize,
          }}
        />
      </div>

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
          onRegisterSigner={canRegisterSigner ? (companyId) => setSignerCompanyId(companyId) : undefined}
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

      {signerCompanyId ? (
        <CompanyMandatarioForm
          variant="hub"
          offices={[{ transitOfficeId, code: office.code, name: office.name }]}
          editing={null}
          initialOfficeIds={[transitOfficeId]}
          restrictToOfficeIds={[transitOfficeId]}
          ownerCompanyIds={[signerCompanyId]}
          overlayClassName="z-[80]"
          onCancel={() => setSignerCompanyId(null)}
          onSubmit={async (input: CompanyMandateSignerInput) => {
            const saved = await createMandateSigner(transitOfficeId, {
              fullName: input.fullName,
              documentType: input.documentType,
              documentNumber: input.documentNumber,
              email: input.email,
              companyTenantIds: [signerCompanyId],
              transitOfficeIds: [transitOfficeId],
              // HU #13132 — modelo, forma de firma y vigencia (el baúl lo resuelve el servidor).
              signerModel: input.signerModel,
              signatureMethod: input.signatureMethod,
              validityKind: input.validityKind,
              validFrom: input.validFrom,
              validTo: input.validTo,
              // HU #13181 — compañías asociadas elegidas en el formulario.
              officeCompanies: input.officeCompanies,
            });
            setSignerCompanyId(null);
            setLastCreatedSignerId(saved.id);
            setSignerEpoch((n) => n + 1);
            show(
              panel?.mode === "mandatario" && !panel.companyId
                ? "Mandatario registrado. Quedó preseleccionado como general del OT; guarda el firmante para fijarlo."
                : "Mandatario registrado. Ya puedes asociarlo como default de la empresa.",
              "success",
            );
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
          overlayClassName="z-[80]"
          onCancel={() => setEditingSigner(null)}
          onSubmit={async (input: CompanyMandateSignerInput) => {
            const saved = await updateMandateSigner(transitOfficeId, editingSigner.id, {
              fullName: input.fullName,
              documentType: input.documentType,
              documentNumber: input.documentNumber,
              email: input.email,
              companyTenantIds: editingSigner.companyTenantIds,
              transitOfficeIds: [transitOfficeId],
              signerModel: input.signerModel,
              signatureMethod: input.signatureMethod,
              validityKind: input.validityKind,
              validFrom: input.validFrom,
              validTo: input.validTo,
            });
            setEditingSigner(null);
            show("Mandatario actualizado.", "success");
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

function signerCell(name: string | null | undefined) {
  const text = name?.trim();
  if (!text) {
    return <span className="text-[#59677D] dark:text-white/55">Sin definir</span>;
  }
  return text;
}

type GeneralMandatarioRow = {
  id: string;
  nit: string | null;
  name: string;
  signerName: string | null;
  docType: string | null;
  docNumber: string | null;
  hash: string | null;
};
