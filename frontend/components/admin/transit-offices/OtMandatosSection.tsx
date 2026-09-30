"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Eye, Pencil } from "lucide-react";
import { CompanyMandatarioForm } from "@/components/admin/companies/mandate-signers/CompanyMandatarioForm";
import { MandatoOtConfigForm, type MandatoOtConfigPanelMode } from "@/components/admin/plataforma/MandatoOtConfigForm";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
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
  fetchMandateSigners,
  type CompanyMandateSignerInput,
  type MandateSigner,
} from "@/lib/api/admin-mandate-signers";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload, isOtAdmin, isSuperAdmin } from "@/lib/auth/jwt";
import { ApiError } from "@/lib/api/types";
import {
  etiquetaTipoFirma,
  tipoDeFirmaMandatario,
} from "@/lib/plataforma/mandatario-firma";


export function OtMandatosSection({ transitOfficeId }: { transitOfficeId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<"loading" | "ready" | "error">("loading");
  const [error, setError] = useState<string | null>(null);
  const [office, setOffice] = useState<MandateOtConfigView | null>(null);
  const [companies, setCompanies] = useState<CompanyOtMandateRuleView[]>([]);
  const [signers, setSigners] = useState<MandateSigner[]>([]);
  const [previewSigner, setPreviewSigner] = useState<MandateSigner | null>(null);
  const [search, setSearch] = useState("");
  // Bug #13055 — tablas homologadas con el modelo de trámites: paginación en cliente con filas por
  // página. El mandatario general es una sola fila fija y no pagina.
  const pgCompanies = usePaginacion();
  const { setPage: setCompanyPage } = pgCompanies;
  const pgSigners = usePaginacion();
  const [panel, setPanel] = useState<{
    mode: MandatoOtConfigPanelMode;
    companyId: string | null;
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

  const filteredCompanies = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return companies;
    return companies.filter((row) => {
      const haystack = [
        row.companyName,
        row.companyTaxId,
        row.defaultMandateSignerName,
      ]
        .filter(Boolean)
        .join(" ")
        .toLowerCase();
      return haystack.includes(q);
    });
  }, [companies, search]);

  useEffect(() => {
    // Reinicia la página al buscar: no es fetch, solo índice de paginación.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reset de paginación al cambiar filtro
    setCompanyPage(1);
  }, [search, setCompanyPage]);

  const columns: DataTableColumn<CompanyOtMandateRuleView>[] = useMemo(
    () => [
      {
        key: "nit",
        header: "NIT",
        cellClassName: "font-mono",
        render: (row) => dash(row.companyTaxId),
      },
      {
        key: "name",
        header: "Empresa",
        cellClassName: "font-semibold",
        render: (row) => row.companyName,
      },
      {
        key: "signer",
        header: "Mandatario",
        render: (row) => signerCell(row.defaultMandateSignerName),
      },
      {
        key: "actions",
        header: "Acción",
        align: "right",
        render: (row) => (
          <RowActions
            actions={[
              {
                icon: Pencil,
                label: `Editar mandatario de ${row.companyName}`,
                tone: "primary",
                onClick: () => setPanel({ mode: "mandatario", companyId: row.companyTenantId }),
              },
            ]}
          />
        ),
      },
    ],
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

  const signerColumns: DataTableColumn<MandateSigner>[] = [
    {
      key: "name",
      header: "Nombre",
      cellClassName: "font-semibold",
      render: (row) => row.fullName,
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
      key: "firma",
      header: "Tipo de firma",
      render: (row) => etiquetaTipoFirma(tipoDeFirmaMandatario(row, transitOfficeId)),
    },
    {
      key: "actions",
      header: "Acción",
      align: "right",
      render: (row) => (
        <RowActions
          actions={[
            {
              icon: Eye,
              label: `Ver firma de ${row.fullName}`,
              tone: "primary",
              onClick: () => setPreviewSigner(row),
            },
          ]}
        />
      ),
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

      <div className="flex flex-col gap-3">
        <div className="flex flex-wrap items-end justify-between gap-2">
          <h3 className="text-sm font-semibold text-[#162244] dark:text-white">
            Empresas que radican
          </h3>
          <label className="min-w-[12rem] flex-1 sm:max-w-xs">
            <span className="sr-only">Buscar empresa</span>
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Buscar empresa o NIT…"
              className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-xs text-[#162244] placeholder:text-[#59677D]/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
              data-testid="ot-mandatos-company-search"
            />
          </label>
        </div>

        <div data-testid="ot-mandatos-company-table">
          <DataTable
            columns={columns}
            rows={pgCompanies.paginar(filteredCompanies)}
            getRowKey={(row) => row.companyTenantId}
            ariaLabel="Empresas que radican en este organismo"
            minWidth={720}
            emptyMessage={
              companies.length === 0
                ? "No hay empresas con este organismo habilitado. Habilita el OT en la ficha de la compañía para que aparezca aquí y puedas registrar su mandato."
                : "Ninguna empresa coincide con la búsqueda."
            }
            pagination={{
              page: pgCompanies.page,
              pageSize: pgCompanies.pageSize,
              totalCount: filteredCompanies.length,
              onPageChange: pgCompanies.setPage,
              onPageSizeChange: pgCompanies.setPageSize,
            }}
          />
        </div>
      </div>

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
