"use client";

import { createElement, useEffect, useState } from "react";
import { CheckCircle2, Clock, FileText, Gauge, MinusCircle, Ticket, type LucideIcon } from "lucide-react";
import { listTenantProducts, setTenantProduct, type TenantProduct } from "@/lib/api/platform";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { ToggleSwitch } from "../ToggleSwitch";

// HU #12967 (Epic #13217) — productos de la FLIT Suite encendidos para la compañía. Lee el estado real
// (platform.tenant_products), no la configuración de la compañía: funciona aunque la compañía no tenga configuración
// guardada. Encender o apagar se aplica al instante, sin «Guardar todo», y queda auditado. Solo SuperAdmin.

const ICONS: Record<string, LucideIcon> = { tramites: FileText, comparendos: Ticket, diagnostico: Gauge };

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
        ariaLabel={`Estado de ${product.name}: ${product.enabled ? "Encendido" : "Apagado"}`}
        label={
          <span className="inline-flex items-center gap-1">
            {product.enabled ? (
              <CheckCircle2 className="h-3.5 w-3.5" aria-hidden />
            ) : (
              <MinusCircle className="h-3.5 w-3.5" aria-hidden />
            )}
            {product.enabled ? "Encendido" : "Apagado"}
          </span>
        }
      />
      {product.comingSoon && (
        <StatusBadge
          tone="neutral"
          ariaLabel={`${product.name}: próximamente`}
          label={
            <span className="inline-flex items-center gap-1">
              <Clock className="h-3.5 w-3.5" aria-hidden />
              Próximamente
            </span>
          }
        />
      )}
    </>
  );
}

export function ProductosTab({ tenantId }: { tenantId: string }) {
  const [products, setProducts] = useState<TenantProduct[] | null>(null);
  const [loadError, setLoadError] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  /** Producto que se va a apagar, esperando confirmación. */
  const [confirmOff, setConfirmOff] = useState<TenantProduct | null>(null);

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
      setProducts(await listTenantProducts(tenantId));
    } catch (e) {
      setError(e instanceof Error && e.message ? e.message : `No se pudo cambiar ${product.name}.`);
    } finally {
      setBusy(null);
    }
  };

  if (loadError) {
    return (
      <p role="alert" className="text-xs font-medium" style={{ color: "#FF4E00" }}>
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
                  <span className="mt-3 grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[#557EFF]/10 text-[#557EFF]">
                    {createElement(ICONS[p.productCode] ?? FileText, { className: "h-4 w-4", "aria-hidden": true })}
                  </span>
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
                      onChange={(on) => (on ? void apply(p, true) : setConfirmOff(p))}
                    />
                  </div>
                </div>
                {confirmOff?.productCode === p.productCode && (
                  <div
                    role="alertdialog"
                    aria-labelledby={`apagar-${p.productCode}`}
                    className="ml-12 rounded-xl border px-4 py-3 text-xs"
                    style={{ borderColor: "#FF4E00" }}
                  >
                    <p id={`apagar-${p.productCode}`} className="font-semibold">
                      ¿Apagar {p.name} para esta compañía?
                    </p>
                    <p className="mt-1 opacity-70">
                      Ningún usuario de la compañía podrá entrar a {p.name} hasta que se vuelva a encender. Sus datos no se
                      borran.
                    </p>
                    <div className="mt-3 flex gap-2">
                      <button
                        type="button"
                        onClick={() => void apply(p, false)}
                        className="rounded-lg px-3 py-1.5 font-semibold text-white"
                        style={{ background: "#FF4E00" }}
                      >
                        Apagar {p.name}
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
        <p role="alert" className="text-xs font-medium" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}
    </div>
  );
}
