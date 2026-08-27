"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { CompanyMandatarioForm } from "@/components/admin/companies/mandate-signers/CompanyMandatarioForm";
import { MandatoOtConfigForm, type MandatoOtConfigPanelMode } from "@/components/admin/plataforma/MandatoOtConfigForm";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import {
  fetchMandateOtConfig,
  listCompanyOtMandateRules,
  type CompanyOtMandateRuleView,
  type MandateOtConfigView,
} from "@/lib/api/admin-plataforma-mandatos";
import {
  createCompanyMandateSigner,
  fetchCompanyTransitOffices,
  fetchRepresentedCompanies,
  type CompanyMandateSignerInput,
  type CompanyTransitOfficeOption,
  type RepresentedCompanyOption,
} from "@/lib/api/admin-mandate-signers";
import { ApiError } from "@/lib/api/types";
import {
  systemTemplateLabel,
  tipoNegocioLabel,
  resolveTipoNegocio,
} from "@/lib/plataforma/mandato-templates";

export function OtMandatosSection({ transitOfficeId }: { transitOfficeId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<"loading" | "ready" | "error">("loading");
  const [error, setError] = useState<string | null>(null);
  const [office, setOffice] = useState<MandateOtConfigView | null>(null);
  const [companies, setCompanies] = useState<CompanyOtMandateRuleView[]>([]);
  const [search, setSearch] = useState("");
  const [panel, setPanel] = useState<{
    mode: MandatoOtConfigPanelMode;
    companyId: string | null;
  } | null>(null);
  const [signerEpoch, setSignerEpoch] = useState(0);
  const [lastCreatedSignerId, setLastCreatedSignerId] = useState<string | null>(null);
  const [signerForm, setSignerForm] = useState<{
    companyId: string;
    offices: CompanyTransitOfficeOption[];
    companies: RepresentedCompanyOption[];
  } | null>(null);

  const load = useCallback(async () => {
    setStatus("loading");
    setError(null);
    try {
      const [view, rules] = await Promise.all([
        fetchMandateOtConfig(transitOfficeId),
        listCompanyOtMandateRules(transitOfficeId),
      ]);
      setOffice(view);
      setCompanies(rules);
      setStatus("ready");
    } catch (err) {
      setOffice(null);
      setCompanies([]);
      setStatus("error");
      setError(err instanceof ApiError ? err.message : "No se pudo cargar la configuración de mandatos.");
    }
  }, [transitOfficeId]);

  const openSignerForm = useCallback(
    async (companyId: string) => {
      try {
        const [offices, represented] = await Promise.all([
          fetchCompanyTransitOffices(companyId),
          fetchRepresentedCompanies(companyId).catch(() => []),
        ]);
        setSignerForm({ companyId, offices, companies: represented });
      } catch (err) {
        show(
          err instanceof ApiError
            ? err.message
            : "No se pudieron cargar los organismos de esa empresa.",
          "error",
        );
      }
    },
    [show],
  );

  useEffect(() => {
    // Carga inicial: status loading/ready vive en este módulo.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- fetch al montar / cambiar id
    void load();
  }, [load]);

  const filteredCompanies = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return companies;
    return companies.filter((row) => row.companyName.toLowerCase().includes(q));
  }, [companies, search]);

  if (status === "loading") {
    return <UiStateBoundary status="loading" skeletonRows={4} />;
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

  const otTipo = tipoNegocioLabel(resolveTipoNegocio(office.assignmentMode));

  return (
    <div className="flex flex-col gap-4" data-testid="ot-mandatos-section">
      <p className="text-xs text-[#59677D] dark:text-white/65">
        El listado son las empresas con este organismo habilitado. Un solo modelo por empresa, para
        todas las familias de trámite. La plantilla del OT se configura aparte.
      </p>

      <div className="rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#0B0F14]">
        <h2 className="text-sm font-bold uppercase tracking-wide text-[#162244] dark:text-white">
          {office.name}
        </h2>
        <dl className="mt-3 grid gap-2 text-xs text-[#59677D] dark:text-white/65 sm:grid-cols-2">
          <div>
            <dt className="font-semibold text-[#162244] dark:text-white">Plantilla del OT</dt>
            <dd>{office.hasCustomTemplate ? "Propia" : office.templateCode}</dd>
          </div>
          <div>
            <dt className="font-semibold text-[#162244] dark:text-white">Modelo del OT</dt>
            <dd>{otTipo}</dd>
          </div>
        </dl>
        <div className="mt-4">
          <button
            type="button"
            onClick={() => setPanel({ mode: "mandato", companyId: null })}
            className="rounded-full px-4 py-2 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
            style={{ background: "linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)" }}
          >
            Configurar mandato
          </button>
        </div>
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
              placeholder="Buscar empresa…"
              className="w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-xs text-[#162244] placeholder:text-[#59677D]/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
              data-testid="ot-mandatos-company-search"
            />
          </label>
        </div>

        {companies.length === 0 ? (
          <UiStateBoundary
            status="empty"
            emptyMessage="No hay empresas con este organismo habilitado. Habilita el OT en la ficha de la compañía para que aparezca aquí y puedas registrar su mandato."
          />
        ) : filteredCompanies.length === 0 ? (
          <UiStateBoundary
            status="empty"
            emptyMessage="Ninguna empresa coincide con la búsqueda."
          />
        ) : (
          <ul className="flex flex-col gap-3" data-testid="ot-mandatos-company-list">
            {filteredCompanies.map((row) => (
              <li key={row.companyTenantId}>
                <CompanyMandateCard
                  row={row}
                  officeTemplateCode={office.templateCode}
                  officeHasCustom={office.hasCustomTemplate}
                  otTipoLabel={otTipo}
                  onConfigure={() =>
                    setPanel({ mode: "mandatario", companyId: row.companyTenantId })
                  }
                  onRegisterSigner={() => void openSignerForm(row.companyTenantId)}
                />
              </li>
            ))}
          </ul>
        )}
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
          onRegisterSigner={(companyId) => void openSignerForm(companyId)}
          onClose={() => setPanel(null)}
          onSaved={(view) => {
            setOffice(view);
            setPanel(null);
            void load();
          }}
        />
      ) : null}

      {signerForm ? (
        <CompanyMandatarioForm
          tenantId={signerForm.companyId}
          offices={signerForm.offices}
          companies={signerForm.companies}
          editing={null}
          initialOfficeIds={[transitOfficeId]}
          restrictToOfficeIds={[transitOfficeId]}
          overlayClassName="z-[80]"
          onCancel={() => setSignerForm(null)}
          onSubmit={async (input: CompanyMandateSignerInput) => {
            const saved = await createCompanyMandateSigner(signerForm.companyId, input);
            setSignerForm(null);
            setLastCreatedSignerId(saved.id);
            setSignerEpoch((n) => n + 1);
            show(
              panel?.mode === "mandato"
                ? "Mandatario registrado. Quedó preseleccionado como default del OT; guarda la plantilla para fijarlo."
                : "Mandatario registrado. Ya puedes asociarlo como default en Persona o RL.",
              "success",
            );
            void load();
            return saved;
          }}
        />
      ) : null}
    </div>
  );
}

function CompanyMandateCard({
  row,
  officeTemplateCode,
  officeHasCustom,
  otTipoLabel,
  onConfigure,
  onRegisterSigner,
}: {
  row: CompanyOtMandateRuleView;
  officeTemplateCode: string;
  officeHasCustom: boolean;
  otTipoLabel: string;
  onConfigure: () => void;
  onRegisterSigner: () => void;
}) {
  const tipo = resolveTipoNegocio(row.assignmentMode);
  const modelo = tipoNegocioLabel(tipo);
  const plantilla =
    tipo === "persona_rl"
      ? "generico"
      : officeHasCustom
        ? "Propia"
        : officeTemplateCode;
  const cta = row.hasExplicitRule ? "Configurar mandato" : "Registrar mandato";

  return (
    <article
      className="rounded-2xl border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#0B0F14]"
      data-testid={`ot-mandatos-company-${row.companyTenantId}`}
    >
      <h2 className="text-sm font-bold uppercase tracking-wide text-[#162244] dark:text-white">
        {row.companyName}
      </h2>
      <dl className="mt-3 grid gap-2 text-xs text-[#59677D] dark:text-white/65 sm:grid-cols-2">
        <div>
          <dt className="font-semibold text-[#162244] dark:text-white">Plantilla</dt>
          <dd>
            {plantilla === "generico" || plantilla === "Propia"
              ? plantilla
              : systemTemplateLabel(plantilla)}
          </dd>
        </div>
        <div>
          <dt className="font-semibold text-[#162244] dark:text-white">Modelo</dt>
          <dd>
            {row.hasExplicitRule ? modelo : `${otTipoLabel} · sin registrar`}
          </dd>
        </div>
      </dl>
      <div className="mt-4 flex flex-wrap gap-2">
        <button
          type="button"
          onClick={onConfigure}
          className="rounded-full px-4 py-2 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          style={{ background: "linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)" }}
        >
          {cta}
        </button>
        <button
          type="button"
          onClick={onConfigure}
          className="rounded-full border border-[#DFE5ED] bg-white px-4 py-2 text-xs font-semibold text-[#162244] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:border-white/15 dark:bg-transparent dark:text-white"
        >
          Tipo por empresa que radica
        </button>
        <button
          type="button"
          onClick={onRegisterSigner}
          className="rounded-full border border-[#DFE5ED] bg-white px-4 py-2 text-xs font-semibold text-[#162244] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:border-white/15 dark:bg-transparent dark:text-white"
        >
          Registrar mandatario
        </button>
      </div>
    </article>
  );
}
