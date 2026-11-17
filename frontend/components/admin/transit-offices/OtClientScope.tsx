"use client";

// Alcance de clientes de un usuario del OT: «Todos los clientes» (por defecto) o «Clientes específicos»
// (selector múltiple con buscador, mínimo 1). Lo usan «Editar usuario» y «Invitar usuario» del hub OT.

import { useCallback, useEffect, useId, useMemo, useState } from "react";
import {
  MultiSelectBuscable,
  type OpcionSeleccionable,
} from "@/components/atom/MultiSelectBuscable";
import {
  fetchOtClientCompanies,
  fetchOtUserClientScope,
  setOtUserClientScope,
  type OtClientCompany,
  type OtClientScopeInput,
  type OtClientScopeMode,
  type OtClientScopeSummary,
} from "@/lib/api/admin-ot-security";
import { ApiError } from "@/lib/api/types";
import {
  EditUserModal,
  type EditUserClientScopeSection,
  type EditUserModalProps,
} from "@/components/atom/modules/users/EditUserModal";

export const CLIENT_SCOPE_EMPTY_MESSAGE =
  "Elige al menos un cliente o vuelve a «Todos los clientes».";
export const CLIENT_SCOPE_ACTOR_RESTRICTED_NOTE =
  "Solo puedes asignar los clientes que tú gestionas.";
export const CLIENT_SCOPE_EXCEEDS_ACTOR_MESSAGE =
  "Solo puedes asignar clientes dentro de los que tú gestionas. Quita los que no te corresponden e inténtalo de nuevo.";
export const CLIENT_SCOPE_SELF_MESSAGE =
  "No puedes cambiar tu propio alcance de clientes. Pídeselo a otro administrador.";

/** Etiqueta corta del listado: «Todos» o «N clientes». */
export function clientScopeLabel(scope?: OtClientScopeSummary | null): string {
  if (!scope || scope.mode !== "especificos") return "Todos los clientes";
  return scope.clientCount === 1 ? "1 cliente" : `${scope.clientCount} clientes`;
}

function codeOf(body: unknown): string | undefined {
  if (body && typeof body === "object") {
    const b = body as { error?: unknown; code?: unknown };
    if (typeof b.error === "string") return b.error;
    if (typeof b.code === "string") return b.code;
  }
  return undefined;
}

/** Mensaje claro para los errores del PUT de alcance. */
export function clientScopeErrorMessage(err: unknown): string {
  if (err instanceof ApiError) {
    const code = codeOf(err.body);
    if (code === "CLIENT_SCOPE_EMPTY") return CLIENT_SCOPE_EMPTY_MESSAGE;
    if (code === "CLIENT_SCOPE_INVALID_CLIENT") {
      return "Alguno de los clientes elegidos ya no pertenece a este organismo. Actualiza la lista e inténtalo de nuevo.";
    }
    if (code === "CLIENT_SCOPE_EXCEEDS_ACTOR") return CLIENT_SCOPE_EXCEEDS_ACTOR_MESSAGE;
    if (code === "CLIENT_SCOPE_SELF") return CLIENT_SCOPE_SELF_MESSAGE;
    if (err.status === 403) return "No tienes permiso para cambiar el alcance de clientes.";
  }
  return "No se pudo guardar el alcance de clientes. Inténtalo de nuevo.";
}

interface CompaniesState {
  options: OpcionSeleccionable[];
  loading: boolean;
  error: boolean;
  actorRestricted: boolean;
  reload: () => void;
}

function useOtClientCompanies(transitOfficeId: string): CompaniesState {
  const [options, setOptions] = useState<OpcionSeleccionable[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const [actorRestricted, setActorRestricted] = useState(false);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial con AbortController
    setLoading(true);
    setError(false);
    fetchOtClientCompanies({ transitOfficeId }, controller.signal)
      .then((res) => {
        if (controller.signal.aborted) return;
        setOptions(
          (res.items ?? []).map((c: OtClientCompany) => ({
            id: c.tenantId,
            label: c.name,
            detalle: c.nit ? `NIT ${c.nit}` : undefined,
            codigo: c.nit ?? undefined,
          })),
        );
        setActorRestricted(res.actorRestricted === true);
        setLoading(false);
      })
      .catch(() => {
        if (controller.signal.aborted) return;
        setError(true);
        setLoading(false);
      });
    return () => controller.abort();
  }, [transitOfficeId, tick]);

  const reload = useCallback(() => setTick((t) => t + 1), []);
  return { options, loading, error, actorRestricted, reload };
}

export interface ClientScopeValue {
  mode: OtClientScopeMode;
  selection: OpcionSeleccionable[];
}

export const CLIENT_SCOPE_DEFAULT: ClientScopeValue = { mode: "todos", selection: [] };

export function toClientScopeInput(value: ClientScopeValue): OtClientScopeInput {
  return {
    mode: value.mode,
    clientTenantIds: value.mode === "especificos" ? value.selection.map((s) => s.id) : [],
  };
}

export function isClientScopeValid(value: ClientScopeValue): boolean {
  return value.mode === "todos" || value.selection.length > 0;
}

/** Radios + selector. Controlado: el estado vive en quien lo usa. */
export function ClientScopeFields({
  transitOfficeId,
  value,
  onChange,
  disabled = false,
  idPrefix,
  preferSpecificWhenRestricted = false,
}: {
  transitOfficeId: string;
  value: ClientScopeValue;
  onChange: (next: ClientScopeValue) => void;
  disabled?: boolean;
  idPrefix: string;
  /** Invitar: si el actor es restringido, el valor por defecto pasa a «Clientes específicos». */
  preferSpecificWhenRestricted?: boolean;
}) {
  const companies = useOtClientCompanies(transitOfficeId);
  const name = `${idPrefix}-mode-${useId()}`;
  const restricted = companies.actorRestricted;
  const noteId = `${idPrefix}-restricted-note`;

  useEffect(() => {
    if (preferSpecificWhenRestricted && restricted && value.mode === "todos") {
      onChange({ mode: "especificos", selection: value.selection });
    }
  }, [preferSpecificWhenRestricted, restricted, value.mode, value.selection, onChange]);
  const showEmpty = value.mode === "especificos" && value.selection.length === 0;

  return (
    <fieldset className="space-y-2" disabled={disabled}>
      <legend className="mb-1 block text-xs font-semibold">Clientes que gestiona</legend>
      <div className="flex flex-wrap gap-x-5 gap-y-1.5">
        <label className="flex cursor-pointer items-center gap-2 text-xs">
          <input
            type="radio"
            name={name}
            checked={value.mode === "todos"}
            disabled={restricted}
            aria-describedby={restricted ? noteId : undefined}
            onChange={() => onChange({ mode: "todos", selection: value.selection })}
            className="h-3.5 w-3.5 accent-flit-brand"
          />
          Todos los clientes
        </label>
        <label className="flex cursor-pointer items-center gap-2 text-xs">
          <input
            type="radio"
            name={name}
            checked={value.mode === "especificos"}
            onChange={() => onChange({ mode: "especificos", selection: value.selection })}
            className="h-3.5 w-3.5 accent-flit-brand"
          />
          Clientes específicos
        </label>
      </div>
      {restricted && (
        <p
          id={noteId}
          className="text-[10px] opacity-70"
          data-testid="client-scope-restricted-note"
        >
          {CLIENT_SCOPE_ACTOR_RESTRICTED_NOTE}
        </p>
      )}
      {value.mode === "especificos" && (
        <div className="space-y-1.5">
          <MultiSelectBuscable
            testId={`${idPrefix}-clientes`}
            grupo="Clientes"
            genero="m"
            opciones={companies.options}
            seleccion={value.selection}
            onChange={(selection) => onChange({ mode: "especificos", selection })}
            buscarLabel="Buscar cliente por nombre o NIT"
            loading={companies.loading}
            error={companies.error}
            onRetry={companies.reload}
            textoVacio="Este organismo aún no tiene compañías cliente."
            textoCargando="Cargando clientes…"
            textoError="No se pudo cargar la lista de clientes."
            disabled={disabled}
          />
          {showEmpty && !companies.loading && !companies.error && (
            <p
              role="alert"
              className="text-[10px] font-medium"
              style={{ color: "var(--destructive)" }}
            >
              {CLIENT_SCOPE_EMPTY_MESSAGE}
            </p>
          )}
        </div>
      )}
      <p className="text-[10px] opacity-60">
        {value.mode === "todos"
          ? "Verá todas las compañías cliente del organismo, también las que se agreguen después."
          : "Solo verá y gestionará los clientes elegidos, en bandeja, tablero y reportes."}
      </p>
    </fieldset>
  );
}

/** Estado + contenido de la sección «Clientes que gestiona» para «Editar usuario». */
export function useOtEditClientScope({
  userId,
  transitOfficeId,
  isSelf,
  enabled,
}: {
  userId: string;
  transitOfficeId: string;
  isSelf: boolean;
  enabled: boolean;
}): EditUserClientScopeSection {
  const [initial, setInitial] = useState<ClientScopeValue | null>(null);
  const [value, setValue] = useState<ClientScopeValue>(CLIENT_SCOPE_DEFAULT);
  const [loadError, setLoadError] = useState(false);
  const [tick, setTick] = useState(0);
  const uid = useId();

  useEffect(() => {
    if (isSelf || !enabled) return;
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial con AbortController
    setLoadError(false);
    setInitial(null);
    fetchOtUserClientScope(userId, { transitOfficeId }, controller.signal)
      .then((res) => {
        if (controller.signal.aborted) return;
        const loaded: ClientScopeValue = {
          mode: res.mode,
          selection: (res.clients ?? []).map((c) => ({ id: c.tenantId, label: c.name })),
        };
        setInitial(loaded);
        setValue(loaded);
      })
      .catch(() => {
        if (!controller.signal.aborted) setLoadError(true);
      });
    return () => controller.abort();
  }, [userId, transitOfficeId, isSelf, enabled, tick]);

  const dirty = useMemo(() => {
    if (!initial) return false;
    if (initial.mode !== value.mode) return true;
    if (value.mode === "todos") return false;
    const a = new Set(initial.selection.map((s) => s.id));
    return a.size !== value.selection.length || value.selection.some((s) => !a.has(s.id));
  }, [initial, value]);

  const content = isSelf ? (
    <div className="space-y-1" data-testid="client-scope-self">
      <p className="text-xs font-semibold">Clientes que gestiona</p>
      <p className="text-[10px] opacity-60">{CLIENT_SCOPE_SELF_MESSAGE}</p>
    </div>
  ) : loadError ? (
    <div className="space-y-1" data-testid="client-scope-error">
      <p className="text-xs font-semibold">Clientes que gestiona</p>
      <div role="alert" className="flex items-center justify-between gap-2 text-xs">
        <span style={{ color: "var(--badge-danger-fg)" }}>
          No se pudo cargar el alcance de clientes.
        </span>
        <button
          type="button"
          className="rounded-full border px-3 py-1 font-semibold"
          onClick={() => setTick((t) => t + 1)}
        >
          Reintentar
        </button>
      </div>
    </div>
  ) : !initial ? (
    <div className="space-y-1" data-testid="client-scope-loading">
      <p className="text-xs font-semibold">Clientes que gestiona</p>
      <p role="status" className="text-xs opacity-70">
        Cargando alcance de clientes…
      </p>
    </div>
  ) : (
    <ClientScopeFields
      transitOfficeId={transitOfficeId}
      value={value}
      onChange={setValue}
      idPrefix={`eu-scope-${uid}`}
    />
  );

  return {
    content,
    canSave: isSelf || !dirty || isClientScopeValid(value),
    onSave: async () => {
      if (isSelf || !dirty) return;
      try {
        await setOtUserClientScope(userId, toClientScopeInput(value), { transitOfficeId });
        setInitial(value);
      } catch (err) {
        throw new Error(clientScopeErrorMessage(err));
      }
    },
  };
}

/**
 * «Editar usuario» del hub OT con la sección «Clientes que gestiona». Solo la ven Admin OT y Super Admin
 * (`canEditClientScope`); el Gestor OT con «Usuarios» delegado no. En la fila propia se explica por qué no se edita.
 */
export function OtEditUserModal({
  transitOfficeId,
  canEditClientScope,
  isSelf,
  ...modalProps
}: Omit<EditUserModalProps, "clientScopeSection"> & {
  transitOfficeId: string;
  canEditClientScope: boolean;
  isSelf: boolean;
}) {
  const section = useOtEditClientScope({
    userId: modalProps.user.id,
    transitOfficeId,
    isSelf,
    enabled: canEditClientScope,
  });
  return (
    <EditUserModal {...modalProps} clientScopeSection={canEditClientScope ? section : undefined} />
  );
}
