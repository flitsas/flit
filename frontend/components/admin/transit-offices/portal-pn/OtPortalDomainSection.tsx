"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { DomainRegisterForm } from "@/components/admin/domain/DomainRegisterForm";
import { DomainStatusPanel, type DomainClient } from "@/components/admin/domain/DomainStatusPanel";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import {
  deletePortalDominio,
  getPortalDominios,
  registerPortalDominio,
  setPortalDominioPrincipal,
  toTenantDomain,
  verifyPortalDominio,
  type DominioPortal,
  type PortalDominioScope,
} from "@/lib/api/admin-ot-portal-dominio";

const FOCO =
  "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";

/**
 * Dominios propios del portal Persona Natural del organismo (Épica #14297, HU #14385). Hasta 10 por organismo, todos
 * llevan al mismo portal y uno es el principal (el que usan el enlace, el QR y los correos). Cada dominio sigue el mismo
 * proceso que marca blanca (DNS, comprobación, certificado, activación) con las pantallas existentes y un cliente
 * inyectado. Sin dominio activo, el portal sigue en el enlace del punto.
 */
export function OtPortalDomainSection({ transitOfficeId }: { transitOfficeId: string }) {
  const [status, setStatus] = useState<UiStatus>("loading");
  const [dominios, setDominios] = useState<DominioPortal[]>([]);
  const [formKey, setFormKey] = useState(0);

  const scope = useMemo<PortalDominioScope>(() => ({ transitOfficeId }), [transitOfficeId]);

  // `silencioso`: tras una acción la lista se refresca sin volver a la pantalla de carga.
  const cargar = useCallback(
    async (silencioso = false) => {
      if (!silencioso) setStatus("loading");
      try {
        const lista = await getPortalDominios(scope);
        setDominios(lista);
        setStatus(lista.length ? "ready" : "empty");
      } catch {
        setStatus("error");
      }
    },
    [scope],
  );

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void cargar();
  }, [cargar]);

  const refrescar = useCallback(() => void cargar(true), [cargar]);

  // El formulario de alta solo agrega: el estado de cada dominio lo muestran sus paneles.
  const clienteAlta = useMemo<DomainClient>(
    () => ({
      get: async () => null,
      register: async (body) => toTenantDomain(await registerPortalDominio(body.host, scope)),
      remove: async () => undefined,
      verify: async () => {
        throw new Error("No aplica al alta de un dominio.");
      },
    }),
    [scope],
  );

  return (
    <section
      className="mt-6 rounded-2xl border bg-card p-4"
      aria-labelledby="portal-domain-title"
    >
      <div className="mb-3">
        <h2
          id="portal-domain-title"
          className="text-sm font-bold"
          style={{ color: "var(--color-flit-primary)" }}
        >
          Dominios propios del portal
        </h2>
        <p className="text-xs opacity-70">
          Publica el portal en una o varias direcciones propias. Todas llevan al mismo portal y el
          dominio principal es el que usan el enlace, el QR y los correos. Mientras no haya uno
          activo, el portal sigue en el enlace del punto.
        </p>
      </div>

      <DomainRegisterForm
        key={formKey}
        tenantId={transitOfficeId}
        purpose="portal"
        label="Agregar un dominio"
        ownerLabel="este organismo"
        client={clienteAlta}
        domain={null}
        onRegistered={() => {
          setFormKey((n) => n + 1);
          refrescar();
        }}
        onRemoved={() => undefined}
      />

      <div className="mt-6">
        <h3 className="mb-2 text-xs font-bold" style={{ color: "var(--color-flit-primary)" }}>
          Dominios del organismo
        </h3>
        {status === "loading" ? (
          <CarLoaderModal label="Cargando los dominios…" />
        ) : (
          <UiStateBoundary
            status={status}
            onRetry={() => void cargar()}
            errorMessage="No se pudieron cargar los dominios."
            emptyMessage="Este organismo aún no tiene un dominio registrado."
          >
            <ul className="flex flex-col gap-4">
              {dominios.map((d) => (
                <li key={d.id}>
                  <DominioItem dominio={d} scope={scope} onChanged={refrescar} />
                </li>
              ))}
            </ul>
          </UiStateBoundary>
        )}
      </div>
    </section>
  );
}

function DominioItem({
  dominio,
  scope,
  onChanged,
}: {
  dominio: DominioPortal;
  scope: PortalDominioScope;
  onChanged: () => void;
}) {
  const [confirmando, setConfirmando] = useState(false);
  const [trabajando, setTrabajando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // El panel pinta estado, motivo exacto y registros DNS; aquí solo se le inyecta este dominio y su comprobación.
  const client = useMemo<DomainClient>(
    () => ({
      get: async () => toTenantDomain(dominio),
      register: async () => toTenantDomain(dominio),
      remove: async () => undefined,
      verify: async () => {
        const verificado = await verifyPortalDominio(dominio.id, scope);
        // Un cambio de estado puede volver principal a este dominio (el primero que se activa): se refresca la lista.
        if (verificado.estado !== dominio.estado) onChanged();
        return toTenantDomain(verificado);
      },
    }),
    [dominio, scope, onChanged],
  );

  async function ejecutar(accion: () => Promise<unknown>) {
    setTrabajando(true);
    setError(null);
    try {
      await accion();
      setConfirmando(false);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo completar la solicitud.");
    } finally {
      setTrabajando(false);
    }
  }

  const puedeHacerPrincipal = dominio.estado === "activo" && !dominio.principal;

  return (
    <article
      className="rounded-xl border p-3"
      style={{ borderColor: "var(--border)" }}
      aria-label={`Dominio ${dominio.host}`}
    >
      {dominio.principal && (
        <p
          className="mb-2 inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-semibold"
          style={{ borderColor: "var(--color-flit-brand)", color: "var(--color-flit-primary)" }}
        >
          <Icono nombre="medalla" className="h-3 w-3" /> Dominio principal
        </p>
      )}

      <DomainStatusPanel mode="admin" client={client} reasonVerbatim />

      <div className="mt-3 flex flex-wrap items-center gap-3">
        {puedeHacerPrincipal && (
          <button
            type="button"
            onClick={() => void ejecutar(() => setPortalDominioPrincipal(dominio.id, scope))}
            disabled={trabajando}
            className={`inline-flex items-center gap-1.5 rounded-full border px-4 py-2 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-50 ${FOCO}`}
            style={{ borderColor: "var(--color-flit-primary)", color: "var(--color-flit-primary)" }}
          >
            <Icono nombre="medalla" className="h-3.5 w-3.5" /> Hacer principal
          </button>
        )}
        <button
          type="button"
          onClick={() => setConfirmando(true)}
          disabled={trabajando}
          className={`inline-flex items-center gap-1.5 rounded-full border px-4 py-2 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-50 ${FOCO}`}
          style={{ borderColor: "var(--destructive)", color: "var(--destructive)" }}
        >
          <Icono nombre="devolucion" className="h-3.5 w-3.5" /> Retirar
        </button>
      </div>

      {error && (
        <p role="alert" className="mt-2 text-xs font-medium" style={{ color: "var(--destructive)" }}>
          {error}
        </p>
      )}

      {confirmando && (
        <div
          className="mt-3 rounded-xl border p-3 text-xs"
          style={{ borderColor: "var(--destructive)" }}
          role="alertdialog"
          aria-label={`Confirmar el retiro de ${dominio.host}`}
        >
          <p style={{ color: "var(--color-flit-primary)" }}>
            ¿Retirar {dominio.host}? El portal sigue en sus otros dominios o en el enlace del punto.
            El registro histórico se conserva.
          </p>
          <div className="mt-2 flex gap-2">
            <button
              type="button"
              onClick={() => setConfirmando(false)}
              disabled={trabajando}
              className={`rounded-full border px-4 py-1.5 font-medium disabled:opacity-60 ${FOCO}`}
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={() => void ejecutar(() => deletePortalDominio(dominio.id, scope))}
              disabled={trabajando}
              className={`rounded-full px-4 py-1.5 font-semibold text-white disabled:opacity-60 ${FOCO}`}
              style={{ background: "var(--destructive)", color: "var(--destructive-foreground)" }}
            >
              {trabajando ? "Retirando…" : "Retirar"}
            </button>
          </div>
        </div>
      )}
    </article>
  );
}
