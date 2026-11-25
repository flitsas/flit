"use client";

import { useEffect, useState } from "react";
import { IconoModulo } from "@flit/ui/Icono";
import { ICONO_TONO, rutaKit } from "@flit/ui/iconos-mapas";
import {
  CODIGO_PREREQUISITO_FALTANTE,
  ETIQUETA_SIN_ADMIN,
  listMyApps,
  listTenantProducts,
  setTenantProduct,
  type TenantProduct,
} from "@/lib/api/platform";
import { ApiError } from "@/lib/api/types";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { ToggleSwitch } from "../ToggleSwitch";
import { CredencialImpuestoDiagnostico } from "./CredencialImpuestoDiagnostico";
import { DialogoActivacionImpuestos } from "./DialogoActivacionImpuestos";
import { EstadoAprovisionamiento, etiquetaEstadoAprovisionamiento } from "./EstadoAprovisionamiento";

// HU #12967 (Epic #13217) — productos de la FLIT Suite encendidos para la compañía. Lee el estado real
// (platform.tenant_products), no la configuración de la compañía: funciona aunque la compañía no tenga configuración
// guardada. Encender o apagar se aplica al instante, sin «Guardar todo», y queda auditado. Solo SuperAdmin.

/** HU #14695 · M19 — producto con aprovisionamiento: se enciende tras un diálogo y muestra el estado de la conexión. */
const IMPUESTOS = "impuestos";

function mensajeErrorActivar(e: unknown): string {
  if (e instanceof ApiError && e.status === 409 && (e.body as { code?: string } | undefined)?.code === CODIGO_PREREQUISITO_FALTANTE) {
    return ETIQUETA_SIN_ADMIN;
  }
  return e instanceof Error && e.message ? e.message : "No se pudo cambiar Impuestos.";
}

const DATE = new Intl.DateTimeFormat("es-CO", { dateStyle: "medium", timeStyle: "short", timeZone: "America/Bogota" });

function lastChange(p: TenantProduct): string {
  if (!p.updatedAt) return "Sin cambios registrados.";
  const when = DATE.format(new Date(p.updatedAt));
  // La hora en es-CO ya termina en punto («a. m.»): no se agrega otro.
  return p.updatedByEmail ? `Último cambio: ${when}, por ${p.updatedByEmail}.` : `Último cambio: ${when}`;
}

/** Estado del producto como chip tintado con icono + texto (nunca solo color). */
function ProductBadges({ product }: { product: TenantProduct }) {
  return (
    <>
      <StatusBadge
        tone={product.enabled ? "success" : "neutral"}
        icono={rutaKit(product.enabled ? ICONO_TONO.ok : ICONO_TONO[""])}
        ariaLabel={`Estado de ${product.name}: ${product.enabled ? "Encendido" : "Apagado"}`}
        label={product.enabled ? "Encendido" : "Apagado"}
      />
      {product.comingSoon && (
        <StatusBadge
          tone="neutral"
          icono={rutaKit(ICONO_TONO[""])}
          ariaLabel={`${product.name}: próximamente`}
          label="Próximamente"
        />
      )}
    </>
  );
}

export interface ProductosTabProps {
  tenantId: string;
  /** Abre «Editar compañía» (acción «Corregir NIT» de Impuestos). Sin ella se indica dónde corregir el NIT. */
  onEditarCompania?: () => void;
}

export function ProductosTab({ tenantId, onEditarCompania }: ProductosTabProps) {
  const [products, setProducts] = useState<TenantProduct[] | null>(null);
  const [loadError, setLoadError] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  /** Producto que se va a apagar, esperando confirmación. */
  const [confirmOff, setConfirmOff] = useState<TenantProduct | null>(null);
  /** Al encender Diagnóstico sin API key de impuesto se sugiere configurarla (opcional, no bloquea). */
  const [sugerirLlave, setSugerirLlave] = useState(false);
  /** Diálogo de activación de Impuestos (alta previa) abierto. */
  const [activarImpuestos, setActivarImpuestos] = useState(false);
  const [activando, setActivando] = useState(false);
  const [errorActivar, setErrorActivar] = useState<string | null>(null);
  /** `url` de Impuestos en me/apps, para enlazar su backoffice. `null` si el usuario no lo tiene o no se pudo leer. */
  const [impuestosUrl, setImpuestosUrl] = useState<string | null>(null);
  const tieneImpuestos = products?.some((p) => p.productCode === IMPUESTOS) ?? false;

  useEffect(() => {
    if (!tieneImpuestos) return;
    let alive = true;
    listMyApps()
      .then((apps) => alive && setImpuestosUrl(apps.find((a) => a.code === IMPUESTOS)?.url || null))
      .catch(() => alive && setImpuestosUrl(null));
    return () => {
      alive = false;
    };
  }, [tieneImpuestos]);

  useEffect(() => {
    let alive = true;
    listTenantProducts(tenantId)
      .then((list) => alive && setProducts(list))
      .catch(() => alive && setLoadError(true));
    return () => {
      alive = false;
    };
  }, [tenantId]);

  const apply = async (product: TenantProduct, enabled: boolean) => {
    setBusy(product.productCode);
    setError(null);
    setConfirmOff(null);
    try {
      await setTenantProduct(tenantId, product.productCode, enabled);
      // Se relee para mostrar quién y cuándo, tal como quedó en la base.
      const list = await listTenantProducts(tenantId);
      setProducts(list);
      const diag = list.find((x) => x.productCode === "diagnostico");
      setSugerirLlave(enabled && product.productCode === "diagnostico" && diag?.impuestoApiKeyConfigurada === false);
    } catch (e) {
      setError(e instanceof Error && e.message ? e.message : `No se pudo cambiar ${product.name}.`);
    } finally {
      setBusy(null);
    }
  };

  const cerrarActivar = () => {
    setActivarImpuestos(false);
    setErrorActivar(null);
  };

  const confirmarActivarImpuestos = async () => {
    setActivando(true);
    setErrorActivar(null);
    try {
      await setTenantProduct(tenantId, IMPUESTOS, true);
      setActivarImpuestos(false);
      setProducts(await listTenantProducts(tenantId));
    } catch (e) {
      setErrorActivar(mensajeErrorActivar(e));
    } finally {
      setActivando(false);
    }
  };

  const impuestos = products?.find((p) => p.productCode === IMPUESTOS);
  const anuncioAprovisionamiento =
    impuestos && (impuestos.enabled || impuestos.provisioning)
      ? `Conexión con el proveedor: ${etiquetaEstadoAprovisionamiento(impuestos.provisioning ?? null)}`
      : "";

  if (loadError) {
    return (
      <p role="alert" className="text-xs font-medium" style={{ color: "var(--destructive)" }}>
        No se pudieron cargar los productos de la compañía. Recarga la página para intentarlo de nuevo.
      </p>
    );
  }

  return (
    <div className="space-y-4">
      <div>
        <h3 className="text-xs font-semibold">Productos de la FLIT Suite</h3>
        <p className="mt-0.5 max-w-2xl text-xs opacity-70">
          Enciende los productos que puede usar esta compañía. El cambio se aplica al instante, sin «Guardar todo», y
          queda en el historial. Con el producto encendido, el Administrador de Compañía entra de una vez; él da acceso a
          los demás usuarios. Si la compañía pertenece a una red, el producto también debe estar encendido en la cabeza.
        </p>
      </div>

      {products === null ? (
        <p className="text-xs opacity-60" aria-busy="true">
          Cargando productos…
        </p>
      ) : (
        <ul className="grid gap-3">
          {products.map((p) => {
            return (
              <li key={p.productCode} className="space-y-2">
                <div className="flex items-start gap-3">
                  <IconoModulo clave={p.productCode} disco size={36} className="mt-3 shrink-0" />
                  <div className="flex-1">
                    <ToggleSwitch
                      id={`producto-${p.productCode}`}
                      label={p.name}
                      badge={<ProductBadges product={p} />}
                      description={
                        (p.comingSoon ? "Próximamente: todavía no está desplegado; sus usuarios verán la pantalla «Próximamente». " : "") +
                        lastChange(p)
                      }
                      checked={p.enabled}
                      disabled={busy !== null}
                      onChange={(on) => {
                        if (!on) setConfirmOff(p);
                        else if (p.productCode === IMPUESTOS) {
                          setConfirmOff(null);
                          setErrorActivar(null);
                          setActivarImpuestos(true);
                        } else void apply(p, true);
                      }}
                    />
                  </div>
                </div>
                {p.productCode === "diagnostico" && typeof p.impuestoApiKeyConfigurada === "boolean" && (
                  <CredencialImpuestoDiagnostico
                    tenantId={tenantId}
                    configurada={p.impuestoApiKeyConfigurada}
                    webhookSecretConfigurada={p.impuestoWebhookSecretConfigurada ?? null}
                    sugerirAbierto={sugerirLlave}
                    onChanged={async () => {
                      setSugerirLlave(false);
                      setProducts(await listTenantProducts(tenantId));
                    }}
                  />
                )}
                {p.productCode === IMPUESTOS && (p.enabled || p.provisioning) && (
                  <EstadoAprovisionamiento
                    tenantId={tenantId}
                    provisioning={p.provisioning ?? null}
                    impuestosUrl={impuestosUrl}
                    onEditarCompania={onEditarCompania}
                    onReintentado={async () => setProducts(await listTenantProducts(tenantId))}
                  />
                )}
                {confirmOff?.productCode === p.productCode && (
                  <div
                    role="alertdialog"
                    aria-labelledby={`apagar-${p.productCode}`}
                    className="ml-12 rounded-xl border px-4 py-3 text-xs"
                    style={{ borderColor: "var(--destructive)" }}
                  >
                    <p id={`apagar-${p.productCode}`} className="font-semibold">
                      {p.productCode === IMPUESTOS ? "¿Suspender Impuestos para esta compañía?" : `¿Apagar ${p.name} para esta compañía?`}
                    </p>
                    <p className="mt-1 opacity-70">
                      {p.productCode === IMPUESTOS
                        ? "Nadie de la compañía podrá entrar ni crear liquidaciones. Los lotes en curso terminan y el historial se conserva. Al volver a encenderlo, sigue con las mismas credenciales."
                        : `Ningún usuario de la compañía podrá entrar a ${p.name} hasta que se vuelva a encender. Sus datos no se borran.`}
                    </p>
                    <div className="mt-3 flex gap-2">
                      <button
                        type="button"
                        onClick={() => void apply(p, false)}
                        className="rounded-lg px-3 py-1.5 font-semibold text-white"
                        style={{ background: "var(--destructive)", color: "var(--destructive-foreground)" }}
                      >
                        {p.productCode === IMPUESTOS ? "Suspender Impuestos" : `Apagar ${p.name}`}
                      </button>
                      <button type="button" onClick={() => setConfirmOff(null)} className="rounded-lg border px-3 py-1.5 font-semibold">
                        Cancelar
                      </button>
                    </div>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}

      {error && (
        <p role="alert" className="text-xs font-medium" style={{ color: "var(--destructive)" }}>
          {error}
        </p>
      )}

      <p role="status" aria-live="polite" aria-atomic="true" className="sr-only" data-testid="anuncio-aprovisionamiento">
        {anuncioAprovisionamiento}
      </p>

      {activarImpuestos && (
        <DialogoActivacionImpuestos
          open
          tenantId={tenantId}
          confirmando={activando}
          error={errorActivar}
          onConfirm={() => void confirmarActivarImpuestos()}
          onCancel={cerrarActivar}
        />
      )}
    </div>
  );
}
