"use client";

import { Pencil, ShieldCheck, UserCheck, UserPlus } from "lucide-react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { MandatarioVigenciaBadge } from "@/components/admin/companies/mandate-signers/MandatarioVigenciaBadge";
import { etiquetaTipoFirma, tipoDeFirmaMandatario } from "@/lib/plataforma/mandatario-firma";
import { etiquetaModelo, modeloDe } from "@/lib/plataforma/mandatario-vigencia";
import { presentarValidacion, requiereValidacionPropia } from "@/lib/plataforma/mandatario-validacion";
import { RowActionsMenu } from "@/components/atom/RowActionsMenu";
import type { RowAction } from "@/components/atom/RowActions";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";

/**
 * Mandatario general del organismo, en una tarjeta destacada (no en una fila de tabla): quién es, su documento,
 * su vigencia, su validación y su forma de firma, con un botón para cambiarlo. Si aún no hay, invita a elegirlo.
 * Debajo de esta tarjeta va el resto de los mandatarios que creó el organismo.
 */
export function MandatarioGeneralCard({
  nombre,
  tipoDocumento,
  numeroDocumento,
  signer,
  puedeEditar,
  acciones,
  onEditar,
}: {
  /** Nombre que guarda la configuración del organismo (sirve aunque la lista de personas aún no lo traiga). */
  nombre: string | null;
  tipoDocumento: string | null;
  numeroDocumento: string | null;
  /** Ficha completa de la persona, si está en la lista de mandatarios del organismo. */
  signer: MandateSigner | null;
  puedeEditar: boolean;
  /** Acciones sobre la persona (editar sus datos, reenviar su validación, desactivar…). */
  acciones: RowAction[];
  onEditar: () => void;
}) {
  const hay = Boolean(nombre?.trim());
  const natural = signer ? modeloDe(signer) === "natural" : false;
  const validacion = signer && requiereValidacionPropia(signer) ? presentarValidacion(signer.identityStatus) : null;

  return (
    <section
      aria-labelledby="mandatario-general-titulo"
      data-testid="ot-mandatos-general-card"
      className="relative overflow-hidden rounded-2xl border border-[#DFE5ED] bg-white p-5 shadow-sm dark:border-white/10 dark:bg-[#0B0F14]"
    >
      <span
        aria-hidden="true"
        className="absolute inset-y-0 left-0 w-1.5"
        style={{ background: "linear-gradient(180deg,#557EFF 0%,#00DBD5 100%)" }}
      />
      <div className="flex flex-col gap-4 pl-2 sm:flex-row sm:items-start sm:justify-between">
        <div className="flex min-w-0 items-start gap-4">
          <span
            aria-hidden="true"
            className="grid h-12 w-12 shrink-0 place-items-center rounded-2xl text-white"
            style={{ background: hay ? "linear-gradient(135deg,#557EFF,#00DBD5)" : "#B7C2D3" }}
          >
            {hay ? <UserCheck className="h-6 w-6" /> : <UserPlus className="h-6 w-6" />}
          </span>
          <div className="min-w-0">
            <p
              id="mandatario-general-titulo"
              className="text-[11px] font-semibold uppercase tracking-wider text-[#557EFF]"
            >
              Mandatario general del organismo
            </p>
            {hay ? (
              <>
                <p
                  className="mt-0.5 truncate text-lg font-bold text-[#162744] dark:text-white"
                  data-testid="ot-mandatos-general-signer"
                >
                  {nombre}
                </p>
                {numeroDocumento ? (
                  <p className="font-mono text-xs text-[#59677D] dark:text-white/65">
                    {tipoDocumento ?? "Documento"} {numeroDocumento}
                  </p>
                ) : null}
                <div className="mt-3 flex flex-wrap items-center gap-2">
                  {signer ? (
                    <>
                      <span className="rounded-full border border-[#DFE5ED] px-2.5 py-0.5 text-[11px] font-semibold text-[#162744] dark:border-white/15 dark:text-white">
                        {etiquetaModelo(signer)}
                      </span>
                      <MandatarioVigenciaBadge signer={signer} />
                      {validacion ? (
                        <StatusBadge
                          tone={validacion.tone}
                          label={validacion.texto}
                          ariaLabel={`Validación: ${validacion.texto}`}
                        />
                      ) : null}
                      {natural ? (
                        <span className="inline-flex items-center gap-1 rounded-full border border-[#DFE5ED] px-2.5 py-0.5 text-[11px] font-semibold text-[#162744] dark:border-white/15 dark:text-white">
                          <ShieldCheck className="h-3.5 w-3.5 text-[#557EFF]" aria-hidden="true" />
                          {etiquetaTipoFirma(tipoDeFirmaMandatario(signer))}
                        </span>
                      ) : null}
                    </>
                  ) : null}
                </div>
              </>
            ) : (
              <p className="mt-0.5 text-sm text-[#59677D] dark:text-white/65" data-testid="ot-mandatos-general-signer">
                Todavía no hay un mandatario general.
              </p>
            )}
            <p className="mt-3 max-w-2xl text-xs leading-relaxed text-[#59677D] dark:text-white/65">
              Firma los trámites de las compañías que no tienen un mandatario propio. Si a una compañía se le eligió
              uno específico, ese tiene prioridad.
            </p>
          </div>
        </div>
        <div className="flex shrink-0 items-center gap-2 self-start">
        {signer && acciones.length > 0 ? (
          <RowActionsMenu
            ariaLabel={`Acciones de ${signer.fullName}`}
            subject={signer.fullName}
            actions={acciones}
            className="w-28"
          />
        ) : null}
        {puedeEditar ? (
          <button
            type="button"
            onClick={onEditar}
            aria-label="Editar mandatario general del organismo"
            className="inline-flex shrink-0 items-center gap-1.5 rounded-full px-4 py-2 text-xs font-semibold text-white"
            style={{ background: "linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)" }}
          >
            <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
            {hay ? "Cambiar mandatario" : "Elegir mandatario"}
          </button>
        ) : null}
        </div>
      </div>
    </section>
  );
}
