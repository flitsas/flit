"use client";

// HU #13443 (Feature #13437, Épica #12750) — Alta, edición y consulta de un rol propio de la compañía.
// - create: código (no editable después), nombre, descripción, producto y permisos.
// - edit: nombre, descripción y permisos de un rol propio.
// - view: rol global de FLIT, solo lectura (la compañía puede asignarlo, no cambiarlo).
// El selector solo ofrece los permisos que el caller puede otorgar (GET /roles/grantable-permissions) y, dentro
// del producto del rol, porque un rol solo lleva permisos de su producto. Los permisos que el rol ya tiene pero
// que el caller ya no puede otorgar se avisan y se quitan al guardar.
import { useEffect, useMemo, useState } from "react";
import { Loader2, Shield } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import {
  createTenantRole,
  getGrantablePermissions,
  getTenantRole,
  setTenantRolePermissions,
  updateTenantRole,
  type GrantablePermission,
  type TenantRole,
  type TenantRoleDetail,
} from "@/lib/api/security";
import { companyRoleErrorMessage } from "./companyRoleErrors";

const INPUT_CLS =
  "h-11 w-full rounded-[10px] border border-[#DFE5ED] bg-white px-3 text-sm text-[#162744] " +
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 " +
  "disabled:bg-[#EEF5FF] dark:border-white/15 dark:bg-[#162744] dark:text-white";
const LABEL_CLS = "flex flex-col gap-1.5 text-sm font-semibold text-[#162744] dark:text-white";
const HINT_CLS = "text-xs font-normal text-[#59677D] dark:text-white/70";
const PRODUCT_LABEL: Record<string, string> = {
  tramites: "Trámites",
  comparendos: "Comparendos",
  diagnostico: "Diagnóstico",
};
const CODE_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_.-]{1,49}$/;

export type CompanyRoleFormMode =
  | { kind: "create" }
  | { kind: "edit"; role: TenantRole }
  | { kind: "view"; role: TenantRole };

export interface CompanyRoleFormModalProps {
  mode: CompanyRoleFormMode;
  onClose: () => void;
  /** Se llama tras guardar con éxito, para recargar el listado. */
  onSaved: () => void;
}

export function CompanyRoleFormModal({ mode, onClose, onSaved }: CompanyRoleFormModalProps) {
  const creating = mode.kind === "create";
  const readOnly = mode.kind === "view";
  const role = mode.kind === "create" ? null : mode.role;

  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [grantable, setGrantable] = useState<GrantablePermission[]>([]);
  const [detail, setDetail] = useState<TenantRoleDetail | null>(null);
  const [code, setCode] = useState(role?.code ?? "");
  const [name, setName] = useState(role?.name ?? "");
  const [description, setDescription] = useState(role?.description ?? "");
  const [productCode, setProductCode] = useState(role?.productCode ?? "tramites");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      try {
        // Los roles globales son de solo lectura: se consulta el detalle, no el selector de otorgables.
        const [perms, det] = await Promise.all([
          readOnly ? Promise.resolve<GrantablePermission[]>([]) : getGrantablePermissions(),
          role ? getTenantRole(role.id) : Promise.resolve<TenantRoleDetail | null>(null),
        ]);
        if (cancelled) return;
        setGrantable(perms);
        setDetail(det);
        if (det) {
          setProductCode(det.productCode);
          setSelected(new Set(det.permissions.map((p) => p.id)));
        } else {
          const products = [...new Set(perms.map((p) => p.productCode))].filter((p) => p !== "plataforma");
          setProductCode(products.includes("tramites") ? "tramites" : (products[0] ?? "tramites"));
        }
      } catch (err) {
        if (!cancelled) setLoadError(companyRoleErrorMessage(err));
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    void load();
    return () => {
      cancelled = true;
    };
  }, [readOnly, role]);

  const products = useMemo(
    () => [...new Set(grantable.map((p) => p.productCode))].filter((p) => p !== "plataforma"),
    [grantable],
  );

  // Solo permisos del producto del rol: un rol no mezcla productos.
  const offered = useMemo(() => grantable.filter((p) => p.productCode === productCode), [grantable, productCode]);
  const byModule = useMemo(() => {
    const map = new Map<string, GrantablePermission[]>();
    for (const p of offered) map.set(p.moduleCode, [...(map.get(p.moduleCode) ?? []), p]);
    return [...map.entries()];
  }, [offered]);

  const offeredIds = useMemo(() => new Set(offered.map((p) => p.id)), [offered]);
  // Permisos que el rol tiene y el caller ya no puede otorgar: se avisan y se quitan al guardar.
  const dropped = !readOnly && detail ? detail.permissions.filter((p) => !offeredIds.has(p.id)) : [];

  function toggle(id: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleModule(ids: string[], on: boolean) {
    setSelected((prev) => {
      const next = new Set(prev);
      for (const id of ids) {
        if (on) next.add(id);
        else next.delete(id);
      }
      return next;
    });
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (readOnly) return;
    setError(null);
    if (creating && !CODE_PATTERN.test(code.trim())) {
      setError("El código admite de 2 a 50 caracteres: letras, números, punto, guion o guion bajo.");
      return;
    }
    if (!name.trim()) {
      setError("El nombre es obligatorio.");
      return;
    }
    const permissionIds = [...selected].filter((id) => offeredIds.has(id));
    setBusy(true);
    try {
      if (creating) {
        await createTenantRole({
          code: code.trim(),
          name: name.trim(),
          description: description.trim() || null,
          productCode,
          permissionIds,
        });
      } else if (role) {
        await updateTenantRole(role.id, { name: name.trim(), description: description.trim() || null });
        await setTenantRolePermissions(role.id, permissionIds);
      }
      onSaved();
    } catch (err) {
      // El formulario conserva lo escrito; solo se muestra el motivo.
      setError(companyRoleErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  const title = creating ? "Nuevo rol" : readOnly ? `Rol «${role?.name}»` : `Editar «${role?.name}»`;
  const description_ = creating
    ? "Rol propio de tu compañía. Solo puedes darle permisos que tú tienes."
    : readOnly
      ? "Rol global de FLIT: lo puedes asignar a tus usuarios, pero no cambiarlo."
      : "El código no se puede cambiar.";

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      title={title}
      description={description_}
      icon={Shield}
      size="lg"
      footer={
        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className="rounded-full border border-[#DFE5ED] px-5 py-2.5 text-sm font-semibold text-[#162744] hover:bg-[#F4F8FF] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:opacity-70 dark:border-white/15 dark:text-white dark:hover:bg-white/5"
          >
            {readOnly ? "Cerrar" : "Cancelar"}
          </button>
          {!readOnly && (
            <button
              type="submit"
              form="company-role-form"
              disabled={busy || loading || loadError !== null}
              className="inline-flex items-center gap-2 rounded-full px-5 py-2.5 text-sm font-semibold text-white shadow-[0_10px_22px_rgba(79,116,201,0.22)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:opacity-70"
              style={{ background: "linear-gradient(135deg, #557EFF 0%, #00DBD5 100%)" }}
            >
              {busy && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
              {creating ? "Crear rol" : "Guardar cambios"}
            </button>
          )}
        </div>
      }
    >
      {loading ? (
        <div role="status" aria-busy="true" className="flex items-center gap-2 py-8 text-sm text-[#59677D] dark:text-white/70">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
          Cargando permisos…
        </div>
      ) : loadError ? (
        <p role="alert" className="py-6 text-sm font-semibold text-[#C2410C]">
          {loadError}
        </p>
      ) : (
        <form id="company-role-form" onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
          <label className={LABEL_CLS}>
            Código
            <input
              value={code}
              onChange={(e) => setCode(e.target.value)}
              disabled={!creating}
              maxLength={50}
              autoComplete="off"
              spellCheck={false}
              className={`${INPUT_CLS} font-mono`}
              aria-describedby="company-role-code-hint"
            />
            {creating && (
              <span id="company-role-code-hint" className={HINT_CLS}>
                Identificador interno, p. ej. contador_senior. No se puede cambiar después.
              </span>
            )}
          </label>
          <label className={LABEL_CLS}>
            Nombre
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              disabled={readOnly}
              maxLength={100}
              className={INPUT_CLS}
            />
          </label>
          <label className={LABEL_CLS}>
            Descripción
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              disabled={readOnly}
              maxLength={500}
              rows={2}
              className={`${INPUT_CLS} h-auto py-2`}
            />
          </label>
          {creating && products.length > 1 && (
            <label className={LABEL_CLS}>
              Producto
              <select
                value={productCode}
                onChange={(e) => {
                  setProductCode(e.target.value);
                  setSelected(new Set());
                }}
                className={INPUT_CLS}
              >
                {products.map((p) => (
                  <option key={p} value={p}>
                    {PRODUCT_LABEL[p] ?? p}
                  </option>
                ))}
              </select>
              <span className={HINT_CLS}>Un rol solo incluye permisos de su producto.</span>
            </label>
          )}

          <fieldset className="flex flex-col gap-3">
            <legend className="mb-1 text-sm font-semibold text-[#162744] dark:text-white">
              Permisos{readOnly && detail ? ` (${detail.permissions.length})` : ""}
            </legend>
            {readOnly ? (
              detail && detail.permissions.length > 0 ? (
                <ul className="flex flex-col gap-1 text-sm text-[#162744] dark:text-white">
                  {detail.permissions.map((p) => (
                    <li key={p.id}>
                      {p.name} <span className="font-mono text-xs text-[#59677D] dark:text-white/70">{p.slug}</span>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="text-sm text-[#59677D] dark:text-white/70">Este rol no tiene permisos asignados.</p>
              )
            ) : byModule.length === 0 ? (
              <p className="rounded-[10px] border border-[#DFE5ED] bg-[#EEF5FF] p-3 text-sm text-[#162744] dark:border-white/15 dark:bg-white/5 dark:text-white">
                No tienes permisos para otorgar en este producto. El rol se puede crear sin permisos y completarlo después.
              </p>
            ) : (
              byModule.map(([moduleCode, perms]) => {
                const ids = perms.map((p) => p.id);
                const allOn = ids.every((id) => selected.has(id));
                return (
                  <div key={moduleCode} className="rounded-[10px] border border-[#DFE5ED] p-3 dark:border-white/15">
                    <label className="flex items-center gap-2 text-sm font-semibold capitalize text-[#162744] dark:text-white">
                      <input
                        type="checkbox"
                        checked={allOn}
                        onChange={(e) => toggleModule(ids, e.target.checked)}
                        aria-label={`Seleccionar todos los permisos de ${moduleCode}`}
                        className="h-4 w-4 accent-[#557EFF]"
                      />
                      {moduleCode}
                    </label>
                    <ul className="mt-2 flex flex-col gap-1.5 pl-6">
                      {perms.map((p) => (
                        <li key={p.id}>
                          <label className="flex items-start gap-2 text-sm text-[#162744] dark:text-white">
                            <input
                              type="checkbox"
                              checked={selected.has(p.id)}
                              onChange={() => toggle(p.id)}
                              className="mt-0.5 h-4 w-4 accent-[#557EFF]"
                            />
                            <span>
                              {p.name} <span className="font-mono text-xs text-[#59677D] dark:text-white/70">{p.slug}</span>
                            </span>
                          </label>
                        </li>
                      ))}
                    </ul>
                  </div>
                );
              })
            )}
          </fieldset>

          {dropped.length > 0 && (
            <p role="status" className="rounded-[10px] bg-[#FFF4E5] p-3 text-xs font-semibold text-[#C2410C]">
              {dropped.length === 1 ? "1 permiso de este rol" : `${dropped.length} permisos de este rol`} ya no se pueden
              otorgar desde tu cuenta y se quitarán al guardar: {dropped.map((p) => p.slug).join(", ")}.
            </p>
          )}

          {error && (
            <p role="alert" className="text-sm font-semibold text-[#C2410C]">
              {error}
            </p>
          )}
        </form>
      )}
    </Modal>
  );
}
