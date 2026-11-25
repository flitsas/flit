"use client";

import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { ActionsMenu, type ActionsMenuItem } from "@flit/ui";
import { Button } from "@flit/ui/Button";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { iconoComo } from "@flit/ui/iconoComo";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Pagination } from "@flit/ui/Pagination";
import { ETIQUETA_PUNTO_CLS, etiquetaKit, type StatusBadgeTone } from "@flit/ui/StatusBadge";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import { useSesion } from "@/components/ImpuestosShell";
import {
  BOTON_REINTENTAR_CLS,
  BOTON_SECUNDARIO_CLS,
  CABECERA_TABLA_CLS,
  CABECERA_TABLA_ESTILO,
  CAMPO_CLS,
  CELDA_TABLA_CLS,
  CONTENEDOR_TABLA_CLS,
  ENLACE_ACCION_CLS,
  ETIQUETA_CLS,
  FILA_TABLA_CLS,
  FILA_TABLA_HOVER_CLS,
  TARJETA_CLS,
  TEXTO_SECUNDARIO_CLS,
} from "@/components/comun/estilos";
import {
  ETIQUETA_USUARIOS_EMPRESA,
  listarEmpresasBackoffice,
  nitVisibleEmpresa,
  nombreVisibleEmpresa,
  type EmpresaIdentificada,
  type PaginaEmpresasBackoffice,
} from "@/lib/backoffice/api";
import { useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";
import { mensajeErrorFijo } from "@/lib/errores";
import { PERMISOS, tienePermiso, type SesionImpuestos } from "@/lib/sesion";
import {
  ERRORES_USUARIOS,
  ESTADOS_USUARIO,
  ROLES_IMPUESTOS,
  TAMANO_PAGINA_USUARIOS,
  etiquetaEstadoUsuario,
  etiquetaRol,
  listarUsuarios,
  rolesAsignables,
  type EstadoUsuario,
  type FiltrosUsuarios,
  type PaginaUsuarios,
  type RolImpuestos,
  type UsuarioImpuestos,
} from "@/lib/usuarios/api";
import { DialogoInvitar } from "./DialogoInvitar";
import { DialogoCambiarRol, DialogoEstadoUsuario, type AccionEstado } from "./DialogosUsuario";

const IconoMas = iconoComo("mas");
const IconoBuscar = iconoComo("buscar");
const IconoUsuarios = iconoComo("usuarios");
const IconoRol = iconoComo("rol");
const IconoPausa = iconoComo("pausa");
const IconoReintento = iconoComo("reintento");

const CELDA_CLS = `${CELDA_TABLA_CLS} ${TABLA_MOVIL_CELL_CLS}`;
/** Columna «Acciones» fija a la derecha cuando hay tabla (≥ 1024 px), como la tabla por defecto. */
const FIJA_CLS = "lg:sticky lg:right-0 lg:bg-card";

/** Tono del estado del usuario (spec-visual §1.6): texto y punto, nunca solo color. */
const TONO_ESTADO: Readonly<Record<string, StatusBadgeTone>> = { ACTIVO: "success", INVITADO: "info", SUSPENDIDO: "warning" };

/**
 * /usuarios (spec-visual §3.8; plan §6.1; D-ROL-ADMIN, D-GESTOR). Una sola ruta:
 * - Administrador Impuestos: los usuarios de su compañía (GET /usuarios, compañía del token).
 * - Super Admin sin ?empresa=: la tabla de compañías con Impuestos; elegir una fija ?empresa= (también en el selector).
 * - Super Admin con ?empresa=: los usuarios de esa compañía, con la miga «Usuarios › {Compañía}» y su NIT, tomados de
 *   PaginaUsuarios.empresa (contrato 1.1.0, delta 7).
 * Filtros Rol y Estado (1.1.0, delta 6) junto a la búsqueda; el AdminCompany (`protegido`) no tiene acciones.
 * Sin impuestos.usuarios.gestionar, la 403. La API vuelve a exigir el permiso en cada ruta.
 */
export function Usuarios() {
  const router = useRouter();
  const { sesion } = useSesion();
  const { empresa, elegir } = useEmpresaSeleccionada();
  const puede = tienePermiso(sesion, PERMISOS.usuariosGestionar);
  const sinAcceso = sesion !== null && !puede;

  useEffect(() => {
    if (sinAcceso) router.replace("/403?code=SIN_PERMISO");
  }, [sinAcceso, router]);

  if (!sesion || !puede) return null;
  const superAdmin = sesion.esSuperAdmin === true;
  const empresaVista = superAdmin ? empresa : null;
  if (superAdmin && !empresaVista) return <CompaniasConImpuestos onElegir={(id) => elegir(id)} />;
  return <VistaUsuarios key={empresaVista ?? "propia"} sesion={sesion} empresa={empresaVista} onVolver={() => elegir(null)} />;
}

type Carga<T> = { tipo: "cargando" } | { tipo: "error"; mensaje: string } | { tipo: "listo"; datos: T };
type Dialogo =
  | { tipo: "invitar"; disparador: HTMLElement | null }
  | { tipo: "rol"; usuario: UsuarioImpuestos; disparador: HTMLElement | null }
  | { tipo: "estado"; accion: AccionEstado; usuario: UsuarioImpuestos; disparador: HTMLElement | null };

function VistaUsuarios({ sesion, empresa, onVolver }: { sesion: SesionImpuestos; empresa: string | null; onVolver: () => void }) {
  const [filtros, setFiltros] = useState<FiltrosUsuarios>({ q: "", rol: null, estado: null, page: 1, pageSize: TAMANO_PAGINA_USUARIOS });
  const [texto, setTexto] = useState("");
  const [carga, setCarga] = useState<Carga<PaginaUsuarios>>({ tipo: "cargando" });
  // Última compañía identificada por la API: sigue en la cabecera mientras se relee la lista con otros filtros.
  const [identificada, setIdentificada] = useState<EmpresaIdentificada | null>(null);
  const [intento, setIntento] = useState(0);
  const [dialogo, setDialogo] = useState<Dialogo | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const regionRef = useRef<HTMLElement>(null);
  const focoPendiente = useRef<{ destino: HTMLElement | null } | null>(null);
  const roles = rolesAsignables(sesion.esSuperAdmin === true);
  const buscarId = useId();
  const rolId = useId();
  const estadoId = useId();

  useEffect(() => {
    const controller = new AbortController();
    listarUsuarios(filtros, empresa, controller.signal).then(
      (datos) => {
        if (controller.signal.aborted) return;
        setCarga({ tipo: "listo", datos });
        if (datos.empresa) setIdentificada(datos.empresa);
      },
      (e: unknown) => {
        if (!controller.signal.aborted) setCarga({ tipo: "error", mensaje: mensajeErrorFijo(e, "Revisa tu conexión e intenta de nuevo.", ERRORES_USUARIOS) });
      },
    );
    return () => controller.abort();
  }, [filtros, empresa, intento]);

  const releer = useCallback(() => setIntento((n) => n + 1), []);
  const aplicar = (cambios: Partial<typeof filtros>) => {
    setCarga({ tipo: "cargando" });
    setFiltros((f) => ({ ...f, ...cambios, page: cambios.page ?? 1 }));
  };
  const buscar = (ev: FormEvent) => {
    ev.preventDefault();
    aplicar({ q: texto.trim() });
  };
  const limpiar = () => {
    setTexto("");
    aplicar({ q: "", rol: null, estado: null });
  };

  /** Cierra el diálogo y, ya pintado, devuelve el foco al disparador; si salió del DOM, a la región de usuarios. */
  const cerrar = (destino: HTMLElement | null) => {
    focoPendiente.current = { destino };
    setDialogo(null);
  };
  useLayoutEffect(() => {
    const pendiente = focoPendiente.current;
    if (!pendiente) return;
    focoPendiente.current = null;
    if (pendiente.destino?.isConnected) pendiente.destino.focus();
    else regionRef.current?.focus();
  });
  const terminar = (destino: HTMLElement | null, texto: string) => {
    cerrar(destino);
    setAviso(texto);
    releer();
  };

  const abrirInvitar = () => {
    setAviso(null);
    setDialogo({ tipo: "invitar", disparador: document.activeElement as HTMLElement | null });
  };

  const alAccion = (accion: "CAMBIAR_ROL" | AccionEstado, usuario: UsuarioImpuestos) => {
    setAviso(null);
    // El menú ya devolvió el foco a su disparador: ese es el destino al cerrar el diálogo.
    const disparador = document.activeElement as HTMLElement | null;
    if (accion === "CAMBIAR_ROL") setDialogo({ tipo: "rol", usuario, disparador });
    else setDialogo({ tipo: "estado", accion, usuario, disparador });
  };

  const hayFiltro = filtros.q !== "" || Boolean(filtros.rol) || Boolean(filtros.estado);
  const nombreEmpresa = empresa && identificada ? nombreVisibleEmpresa(identificada, empresa) : null;
  const descripcion = empresa
    ? nombreEmpresa
      ? [nombreEmpresa, nitVisibleEmpresa(identificada)].filter(Boolean).join(" · ")
      : undefined
    : sesion.empresa.nombre || undefined;
  let contenido: ReactNode;
  if (carga.tipo === "cargando") contenido = <EsqueletoTabla etiqueta="Cargando usuarios…" />;
  else if (carga.tipo === "error")
    contenido = (
      <InlineAlert
        tone="error"
        title="No pudimos cargar los usuarios"
        action={
          <button
            type="button"
            className={BOTON_REINTENTAR_CLS}
            onClick={() => {
              setCarga({ tipo: "cargando" });
              releer();
            }}
          >
            Reintentar
          </button>
        }
      >
        {carga.mensaje}
      </InlineAlert>
    );
  else if (carga.datos.items.length === 0 && hayFiltro)
    contenido = (
      <EstadoVacio
        anunciar
        nivelTitulo={3}
        icono={<IconoBuscar className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
        titulo="Ningún usuario coincide"
        mensaje="Prueba con otro nombre o correo, o quita algún filtro."
        acciones={[
          <button key="limpiar" type="button" onClick={limpiar} className={BOTON_SECUNDARIO_CLS}>
            Limpiar filtros
          </button>,
        ]}
      />
    );
  else if (carga.datos.items.length === 0)
    contenido = (
      <EstadoVacio
        nivelTitulo={3}
        icono={<IconoUsuarios className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
        titulo="Aún no hay usuarios en Impuestos"
        acciones={[
          <Button key="invitar" onClick={abrirInvitar}>
            Invitar usuario
          </Button>,
        ]}
      />
    );
  else {
    const { datos } = carga;
    contenido = (
      <div className="flex flex-col gap-3">
        <TablaUsuarios usuarios={datos.items} propio={sesion.usuario.id} roles={roles} onAccion={alAccion} />
        <Pagination
          page={datos.page}
          pageSize={filtros.pageSize}
          totalCount={datos.total}
          onPageChange={(page) => aplicar({ page })}
          ariaLabel="Paginación de usuarios"
          noun="usuarios"
        />
      </div>
    );
  }

  return (
    <>
      <CabeceraModulo
        titulo="Usuarios"
        icono="colaboradores"
        miga={nombreEmpresa ? [{ etiqueta: "Usuarios", onClick: onVolver }, { etiqueta: nombreEmpresa }] : undefined}
        descripcion={descripcion}
        acciones={
          <Button onClick={abrirInvitar} className="gap-1.5">
            <IconoMas className="h-4 w-4" aria-hidden="true" />
            Invitar usuario
          </Button>
        }
      />
      {/* Región viva estable: el resultado de invitar, cambiar rol, suspender o reactivar. */}
      <div role="status" aria-live="polite" aria-atomic="true">
        {aviso && (
          <InlineAlert tone="success" title="Listo" vivo={false}>
            {aviso}
          </InlineAlert>
        )}
      </div>
      <section ref={regionRef} tabIndex={-1} aria-label="Usuarios de la compañía" className={`${TARJETA_CLS} flex flex-col gap-4 outline-none`}>
        <form role="search" aria-label="Buscar usuarios" onSubmit={buscar} className="flex flex-wrap items-end gap-2">
          <div className="flex min-w-0 flex-1 flex-col gap-1 sm:max-w-sm">
            <label htmlFor={buscarId} className={ETIQUETA_CLS}>
              Buscar nombre o correo
            </label>
            <input id={buscarId} type="search" value={texto} maxLength={100} onChange={(e) => setTexto(e.target.value)} className={CAMPO_CLS} />
          </div>
          <button type="submit" className={BOTON_SECUNDARIO_CLS}>
            Buscar
          </button>
          <div className="flex flex-col gap-1">
            <label htmlFor={rolId} className={ETIQUETA_CLS}>
              Rol
            </label>
            <select
              id={rolId}
              value={filtros.rol ?? ""}
              onChange={(e) => aplicar({ rol: (e.target.value || null) as RolImpuestos | null })}
              className={`${CAMPO_CLS} w-60`}
            >
              <option value="">Todos</option>
              {ROLES_IMPUESTOS.map((r) => (
                <option key={r} value={r}>
                  {etiquetaRol(r)}
                </option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-1">
            <label htmlFor={estadoId} className={ETIQUETA_CLS}>
              Estado
            </label>
            <select
              id={estadoId}
              value={filtros.estado ?? ""}
              onChange={(e) => aplicar({ estado: (e.target.value || null) as EstadoUsuario | null })}
              className={`${CAMPO_CLS} w-44`}
            >
              <option value="">Todos</option>
              {ESTADOS_USUARIO.map((e) => (
                <option key={e} value={e}>
                  {etiquetaEstadoUsuario(e)}
                </option>
              ))}
            </select>
          </div>
        </form>
        <div>{contenido}</div>
      </section>
      {dialogo?.tipo === "invitar" && (
        <DialogoInvitar
          empresa={empresa}
          roles={roles}
          onCerrar={() => cerrar(dialogo.disparador)}
          onInvitado={() => terminar(dialogo.disparador, "Invitación enviada.")}
        />
      )}
      {dialogo?.tipo === "rol" && (
        <DialogoCambiarRol
          usuario={dialogo.usuario}
          empresa={empresa}
          roles={roles}
          onCerrar={() => cerrar(dialogo.disparador)}
          onCambiado={(texto) => terminar(dialogo.disparador, texto)}
        />
      )}
      {dialogo?.tipo === "estado" && (
        <DialogoEstadoUsuario
          usuario={dialogo.usuario}
          accion={dialogo.accion}
          empresa={empresa}
          onCerrar={() => cerrar(dialogo.disparador)}
          onHecho={(texto) => terminar(dialogo.disparador, texto)}
        />
      )}
    </>
  );
}

interface TablaUsuariosProps {
  usuarios: UsuarioImpuestos[];
  /** Id del usuario de la sesión: su fila no tiene acciones. */
  propio: string;
  roles: readonly RolImpuestos[];
  onAccion: (accion: "CAMBIAR_ROL" | AccionEstado, usuario: UsuarioImpuestos) => void;
}

/**
 * Tabla por defecto: Nombre · Correo · Rol · Estado · Acciones. Las acciones salen del estado (Activo: Cambiar rol si hay
 * otro rol que dar, y Suspender; Suspendido: Reactivar). La fila propia dice «Eres tú»; la del AdminCompany (`protegido`,
 * 1.1.0) el literal OBJETIVO_PROTEGIDO del contrato, y la invitación pendiente el suyo, todas sin menú. Por debajo de
 * 1024 px cada usuario es una tarjeta con su nombre de título.
 */
function TablaUsuarios({ usuarios, propio, roles, onAccion }: TablaUsuariosProps) {
  return (
    <div className={CONTENEDOR_TABLA_CLS}>
      <table aria-label="Usuarios de Impuestos" className={`w-full min-w-[720px] border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
        <thead className={TABLA_MOVIL_THEAD_CLS}>
          <tr>
            {["Nombre", "Correo", "Rol", "Estado", "Acciones"].map((t) => (
              <th key={t} scope="col" className={`${CABECERA_TABLA_CLS} ${t === "Acciones" ? FIJA_CLS : ""}`} style={CABECERA_TABLA_ESTILO}>
                {t}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className={TABLA_MOVIL_TBODY_CLS}>
          {usuarios.map((u) => (
            <tr key={u.id} className={`${FILA_TABLA_CLS} ${FILA_TABLA_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS}`}>
              <td data-label="Nombre" className={`${CELDA_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS} font-semibold`}>
                {u.nombre}
              </td>
              <td data-label="Correo" className={`${CELDA_CLS} break-all`}>
                {u.email}
              </td>
              <td data-label="Rol" className={CELDA_CLS}>
                {etiquetaRol(u.rol)}
              </td>
              <td data-label="Estado" className={CELDA_CLS}>
                <span {...etiquetaKit(TONO_ESTADO[u.estado] ?? "neutral")}>
                  <span className={ETIQUETA_PUNTO_CLS} aria-hidden="true" />
                  {etiquetaEstadoUsuario(u.estado)}
                </span>
              </td>
              <td data-label="Acciones" className={`${CELDA_CLS} ${FIJA_CLS}`}>
                <AccionesUsuario usuario={u} propio={u.id === propio} roles={roles} onAccion={onAccion} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function AccionesUsuario({ usuario, propio, roles, onAccion }: { usuario: UsuarioImpuestos; propio: boolean; roles: readonly RolImpuestos[]; onAccion: TablaUsuariosProps["onAccion"] }) {
  if (propio) return <span className={TEXTO_SECUNDARIO_CLS}>Eres tú</span>;
  if (usuario.protegido === true) return <span className={TEXTO_SECUNDARIO_CLS}>{ERRORES_USUARIOS.OBJETIVO_PROTEGIDO}</span>;
  if (usuario.estado === "INVITADO") return <span className={TEXTO_SECUNDARIO_CLS}>{ERRORES_USUARIOS.INVITACION_PENDIENTE}</span>;
  const items: ActionsMenuItem[] = [];
  if (usuario.estado === "ACTIVO") {
    if (roles.some((r) => r !== usuario.rol)) items.push({ key: "rol", label: "Cambiar rol", icon: IconoRol, onSelect: () => onAccion("CAMBIAR_ROL", usuario) });
    items.push({ key: "suspender", label: "Suspender", icon: IconoPausa, tone: "danger", onSelect: () => onAccion("SUSPENDER", usuario) });
  } else if (usuario.estado === "SUSPENDIDO") {
    items.push({ key: "reactivar", label: "Reactivar", icon: IconoReintento, onSelect: () => onAccion("REACTIVAR", usuario) });
  }
  if (items.length === 0) return null;
  return <ActionsMenu ariaLabel="Acciones del usuario" items={items} />;
}

/** Cargando: filas grises con la forma de la tabla y un `role="status"` con su texto; sin animación con menos movimiento. */
function EsqueletoTabla({ etiqueta, filas = 5 }: { etiqueta: string; filas?: number }) {
  return (
    <div role="status" aria-label={etiqueta} className="overflow-hidden rounded-xl border border-border">
      <span className="sr-only">{etiqueta}</span>
      <div aria-hidden="true" className="h-9" style={CABECERA_TABLA_ESTILO} />
      {Array.from({ length: filas }, (_, i) => (
        <div key={i} aria-hidden="true" className={`${FILA_TABLA_CLS} flex items-center gap-4 bg-card px-3 py-3`}>
          <span className="h-3 w-1/4 rounded-full bg-[var(--di-surface-sunken)] motion-safe:animate-pulse" />
          <span className="h-3 w-1/3 rounded-full bg-[var(--di-surface-sunken)] motion-safe:animate-pulse" />
          <span className="h-3 w-1/6 rounded-full bg-[var(--di-surface-sunken)] motion-safe:animate-pulse" />
        </div>
      ))}
    </div>
  );
}

/**
 * Super Admin sin compañía elegida: las compañías con Impuestos (GET /backoffice/empresas) con su número de usuarios
 * (`usuarios`, 1.1.0; celda vacía si no llega) y «Ver usuarios», que fija ?empresa=.
 */
function CompaniasConImpuestos({ onElegir }: { onElegir: (id: string) => void }) {
  const [page, setPage] = useState(1);
  const [carga, setCarga] = useState<Carga<PaginaEmpresasBackoffice>>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    listarEmpresasBackoffice({ q: "", page }, controller.signal).then(
      (datos) => {
        if (!controller.signal.aborted) setCarga({ tipo: "listo", datos });
      },
      () => {
        if (!controller.signal.aborted) setCarga({ tipo: "error", mensaje: "Revisa tu conexión e intenta de nuevo." });
      },
    );
    return () => controller.abort();
  }, [page, intento]);

  let contenido: ReactNode;
  if (carga.tipo === "cargando") contenido = <EsqueletoTabla etiqueta="Cargando compañías…" />;
  else if (carga.tipo === "error")
    contenido = (
      <InlineAlert
        tone="error"
        title="No se pudieron cargar las compañías"
        action={
          <button
            type="button"
            className={BOTON_REINTENTAR_CLS}
            onClick={() => {
              setCarga({ tipo: "cargando" });
              setIntento((n) => n + 1);
            }}
          >
            Reintentar
          </button>
        }
      >
        {carga.mensaje}
      </InlineAlert>
    );
  else if (carga.datos.items.length === 0)
    contenido = <EstadoVacio nivelTitulo={3} icono={<IconoUsuarios className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />} titulo="Ninguna compañía tiene Impuestos activo todavía" />;
  else {
    const { datos } = carga;
    contenido = (
      <div className="flex flex-col gap-3">
        <div className={CONTENEDOR_TABLA_CLS}>
          <table aria-label="Compañías con Impuestos" className={`w-full min-w-[560px] border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
            <thead className={TABLA_MOVIL_THEAD_CLS}>
              <tr>
                {["Compañía", "NIT", ETIQUETA_USUARIOS_EMPRESA, "Acción"].map((t) => (
                  <th key={t} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                    {t}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className={TABLA_MOVIL_TBODY_CLS}>
              {datos.items.map((e) => (
                <tr key={e.id} className={`${FILA_TABLA_CLS} ${FILA_TABLA_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS}`}>
                  <td data-label="Compañía" className={`${CELDA_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS} font-semibold`}>
                    {e.nombre}
                  </td>
                  <td data-label="NIT" className={`${CELDA_CLS} tabular-nums`}>
                    {e.nit ?? <span className={TEXTO_SECUNDARIO_CLS}>Sin dato</span>}
                  </td>
                  <td data-label={ETIQUETA_USUARIOS_EMPRESA} className={`${CELDA_CLS} tabular-nums`}>
                    {typeof e.usuarios === "number" ? e.usuarios : null}
                  </td>
                  <td data-label="Acción" className={CELDA_CLS}>
                    <button type="button" className={ENLACE_ACCION_CLS} onClick={() => onElegir(e.id)}>
                      Ver usuarios <span className="sr-only">{`de ${e.nombre}`}</span>
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <Pagination page={datos.page} pageSize={datos.pageSize} totalCount={datos.total} onPageChange={setPage} ariaLabel="Paginación de compañías" noun="compañías" />
      </div>
    );
  }

  return (
    <>
      <CabeceraModulo titulo="Usuarios" icono="colaboradores" descripcion="Todas las compañías" />
      <section aria-label="Compañías con Impuestos" className={`${TARJETA_CLS} flex flex-col gap-4`}>
        {contenido}
      </section>
    </>
  );
}
