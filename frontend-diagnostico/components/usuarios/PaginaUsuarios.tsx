"use client";

import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Button, buttonClass } from "@flit/ui/Button";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { iconoComo } from "@flit/ui/iconoComo";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Pagination } from "@flit/ui/Pagination";
import { useSesion } from "@/components/DiagnosticoShell";
import { Migas } from "@/components/comun/Migas";
import { BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, ENLACE_ACCION_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { ApiError } from "@/lib/api";
import { puedeGestionarUsuarios, type SesionDiagnostico } from "@/lib/sesion";
import { accesoNegadoUsuarios, listarUsuarios, traducirErrorUsuarios } from "@/lib/usuarios/api";
import {
  AVISO_INVITACION,
  TAMANO_POR_DEFECTO,
  type AccionUsuario,
  type EmpresaCabecera,
  type FiltroConUsuarios,
  type FiltrosEmpresas,
  type FiltrosUsuarios,
  type ListaUsuarios,
  type RolDiagnostico,
  type UsuarioDiagnostico,
} from "@/lib/usuarios/tipos";
import { DialogoConfirmarEstado, type AccionEstado } from "./DialogoConfirmarEstado";
import { FiltrosUsuarios as BarraFiltros, hayFiltrosUsuarios, type ValorFiltros } from "./FiltrosUsuarios";
import { ModalCambiarRol } from "./ModalCambiarRol";
import { ModalInvitarUsuario } from "./ModalInvitarUsuario";
import { TablaCompanias, consultaEmpresas } from "./TablaCompanias";
import { EsqueletoTabla, TablaUsuarios } from "./TablaUsuarios";

const IconoBuscar = iconoComo("buscar");
const IconoUsuarios = iconoComo("usuarios");

/** Filtros de la tabla de compañías desde la URL (solo códigos y el texto buscado, SP-21). */
function filtrosEmpresasDe(params: URLSearchParams): FiltrosEmpresas {
  const usuarios = params.get("usuarios");
  const pagina = Number.parseInt(params.get("pagina") ?? "", 10);
  const tamano = Number.parseInt(params.get("tamano") ?? "", 10);
  return {
    busqueda: params.get("busqueda") ?? "",
    usuarios: usuarios === "CON" || usuarios === "SIN" ? (usuarios as FiltroConUsuarios) : null,
    pagina: Number.isFinite(pagina) && pagina > 0 ? pagina : 1,
    tamano: Number.isFinite(tamano) && tamano > 0 ? tamano : TAMANO_POR_DEFECTO,
  };
}

function urlEmpresas(f: FiltrosEmpresas): string {
  const q = consultaEmpresas(f);
  if (f.pagina > 1) q.set("pagina", String(f.pagina));
  if (f.tamano !== TAMANO_POR_DEFECTO) q.set("tamano", String(f.tamano));
  const texto = q.toString();
  return texto ? `/usuarios?${texto}` : "/usuarios";
}

/**
 * Módulo Usuarios de Diagnóstico (HU #14588, spec-ui §10 con los ajustes H-UI del contrato). Una sola ruta, /usuarios:
 * - Admin de compañía (gestionarUsuarios, no SuperAdmin): sus usuarios con GET /usuarios (tenant del token); el
 *   parámetro `empresa` de la URL se ignora.
 * - SuperAdmin: sin `?empresa`, la tabla de compañías (GET /backoffice/usuarios/empresas); con `?empresa=<id>`, los
 *   usuarios de esa compañía por el grupo del backoffice, con migas «Usuarios › {Compañía}».
 * Sin la capacidad, la página 403. La API vuelve a exigir el permiso en cada ruta (SP-17).
 */
export function PaginaUsuarios() {
  const router = useRouter();
  const params = useSearchParams();
  const { sesion } = useSesion();
  const puede = puedeGestionarUsuarios(sesion?.capacidades);
  const sinAcceso = sesion !== null && !puede;

  useEffect(() => {
    if (sinAcceso) router.replace("/403?code=SIN_PERMISO");
  }, [sinAcceso, router]);

  if (!sesion || !puede) return null;
  const superAdmin = sesion.usuario.esSuperAdmin === true;
  const empresa = superAdmin ? params.get("empresa") : null;
  const filtrosEmpresas = filtrosEmpresasDe(new URLSearchParams(params.toString()));

  if (superAdmin && !empresa) {
    return (
      <>
        <CabeceraModulo titulo="Usuarios" descripcion="Compañías con Diagnóstico activo" icono="colaboradores" />
        <TablaCompanias
          filtros={filtrosEmpresas}
          onCambiar={(cambios) => router.replace(urlEmpresas({ ...filtrosEmpresas, ...cambios }), { scroll: false })}
        />
      </>
    );
  }
  return <VistaUsuarios key={empresa ?? "propia"} sesion={sesion} empresa={empresa} volver={urlEmpresas({ ...filtrosEmpresas, pagina: 1 })} />;
}

type Carga = { tipo: "cargando" } | { tipo: "error"; error: unknown } | { tipo: "listo"; lista: ListaUsuarios };
type Dialogo =
  | { tipo: "invitar" }
  | { tipo: "rol"; usuario: UsuarioDiagnostico; disparador: HTMLButtonElement | null }
  | { tipo: "estado"; accion: AccionEstado; usuario: UsuarioDiagnostico; disparador: HTMLButtonElement | null };

const FILTROS_INICIALES: FiltrosUsuarios = { busqueda: "", rol: null, estado: null, pagina: 1, tamano: TAMANO_POR_DEFECTO };

function VistaUsuarios({ sesion, empresa, volver }: { sesion: SesionDiagnostico; empresa: string | null; volver: string }) {
  const router = useRouter();
  const [filtros, setFiltros] = useState<FiltrosUsuarios>(FILTROS_INICIALES);
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const [roles, setRoles] = useState<RolDiagnostico[]>([]);
  const [dialogo, setDialogo] = useState<Dialogo | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  // D-6: nombre y NIT de la compañía del backoffice vienen en `empresa` de la propia lista (se conserva el último).
  const [empresaVista, setEmpresaVista] = useState<EmpresaCabecera | null>(null);
  const invitarRef = useRef<HTMLButtonElement>(null);

  const clave = JSON.stringify(filtros);

  useEffect(() => {
    const control = new AbortController();
    listarUsuarios(empresa, JSON.parse(clave) as FiltrosUsuarios, control.signal).then(
      (lista) => {
        if (control.signal.aborted) return;
        setCarga({ tipo: "listo", lista });
        setRoles(lista.roles_asignables);
        if (lista.empresa) setEmpresaVista(lista.empresa);
      },
      (error: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error });
      },
    );
    return () => control.abort();
  }, [empresa, clave, intento]);

  const acceso = carga.tipo === "error" ? accesoNegadoUsuarios(carga.error) : null;
  useEffect(() => {
    if (acceso) router.replace(`/403?code=${encodeURIComponent(acceso)}`);
  }, [acceso, router]);

  const releer = useCallback(() => setIntento((n) => n + 1), []);
  const aplicar = (cambios: Partial<FiltrosUsuarios>) => {
    setFiltros((f) => ({ ...f, ...cambios, pagina: cambios.pagina ?? 1 }));
    setCarga({ tipo: "cargando" });
  };
  const limpiar = () => aplicar({ busqueda: "", rol: null, estado: null });
  const valor: ValorFiltros = { busqueda: filtros.busqueda, rol: filtros.rol, estado: filtros.estado };
  const conFiltros = hayFiltrosUsuarios(valor);

  /** Cierra el diálogo y devuelve el foco a su disparador (el retorno por defecto no sobrevive a la fila re-pintada). */
  const cerrar = (destino?: HTMLElement | null) => {
    setDialogo(null);
    if (destino?.isConnected) destino.focus();
  };
  const actualizarFila = (fila: UsuarioDiagnostico) =>
    setCarga((c) => (c.tipo === "listo" ? { tipo: "listo", lista: { ...c.lista, usuarios: c.lista.usuarios.map((u) => (u.id === fila.id ? fila : u)) } } : c));

  const alAccion = (accion: AccionUsuario, usuario: UsuarioDiagnostico, disparador: HTMLButtonElement | null) => {
    setAviso(null);
    if (accion === "CAMBIAR_ROL") setDialogo({ tipo: "rol", usuario, disparador });
    else setDialogo({ tipo: "estado", accion, usuario, disparador });
  };

  const botonInvitar = (
    // Botón nativo con la clase del kit: `Button` no expone `ref` y el foco debe volver aquí al cerrar «Invitar usuario».
    <button
      ref={invitarRef}
      type="button"
      className={buttonClass({})}
      disabled={roles.length === 0}
      onClick={() => {
        setAviso(null);
        setDialogo({ tipo: "invitar" });
      }}
    >
      Invitar usuario
    </button>
  );

  let contenido: ReactNode;
  if (carga.tipo === "cargando" || acceso) {
    contenido = <EsqueletoTabla etiqueta="Cargando usuarios…" />;
  } else if (carga.tipo === "error") {
    const noEncontrada = carga.error instanceof ApiError && carga.error.code === "EMPRESA_NO_ENCONTRADA";
    contenido = (
      <InlineAlert
        tone="error"
        title="No pudimos cargar los usuarios"
        action={
          noEncontrada ? (
            <Link href={volver} className={ENLACE_ACCION_CLS}>
              Volver a compañías
            </Link>
          ) : (
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
          )
        }
      >
        {traducirErrorUsuarios(carga.error, "Revisa tu conexión e intenta de nuevo.")}
      </InlineAlert>
    );
  } else if (carga.lista.usuarios.length === 0 && conFiltros) {
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
  } else if (carga.lista.usuarios.length === 0) {
    contenido = (
      <EstadoVacio
        nivelTitulo={3}
        icono={<IconoUsuarios className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
        titulo="Aún no hay usuarios en Diagnóstico"
        mensaje="Invita a las personas de la compañía que van a crear o consultar diagnósticos."
        acciones={
          roles.length > 0
            ? [
                <Button key="invitar" onClick={() => setDialogo({ tipo: "invitar" })}>
                  Invitar usuario
                </Button>,
              ]
            : undefined
        }
      />
    );
  } else {
    const { lista } = carga;
    contenido = (
      <div className="flex flex-col gap-3">
        <p className="text-sm text-muted-foreground">{`Mostrando ${lista.usuarios.length} de ${lista.total} usuarios`}</p>
        <TablaUsuarios usuarios={lista.usuarios} onAccion={alAccion} />
        <Pagination
          page={lista.pagina}
          pageSize={filtros.tamano}
          totalCount={lista.total}
          onPageChange={(pagina) => aplicar({ pagina })}
          onPageSizeChange={(tamano) => aplicar({ tamano, pagina: 1 })}
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
        encima={
          empresa ? (
            empresaVista ? (
              <Migas migas={[{ texto: "Usuarios", href: volver }, { texto: empresaVista.nombre }]} />
            ) : (
              // D-6: sin `empresa` en la respuesta (back anterior) o mientras llega, esqueleto; nunca un nombre genérico.
              <span role="status" aria-label="Cargando la compañía…" className="block h-4 w-48 rounded-full bg-[var(--di-surface-sunken)] motion-safe:animate-pulse" />
            )
          ) : undefined
        }
        descripcion={
          empresa
            ? empresaVista
              ? `${empresaVista.nombre}${empresaVista.nit ? ` · NIT ${empresaVista.nit}` : ""}`
              : undefined
            : sesion.empresa.nombre || undefined
        }
        acciones={botonInvitar}
      />
      {/* Región viva estable: el aviso de invitar (siempre el mismo, SP-15) y el resultado de suspender o reactivar. */}
      <div role="status" aria-live="polite" aria-atomic="true">
        {aviso && (
          <InlineAlert tone="success" title="Listo">
            {aviso}
          </InlineAlert>
        )}
      </div>
      <section aria-label="Usuarios de la compañía" className={`${TARJETA_CLS} flex flex-col gap-4`}>
        <BarraFiltros valor={valor} onCambiar={aplicar} onLimpiar={conFiltros ? limpiar : undefined} />
        <div>{contenido}</div>
      </section>
      {dialogo?.tipo === "invitar" && (
        <ModalInvitarUsuario
          abierto
          empresa={empresa}
          rolesAsignables={roles}
          onCerrar={() => cerrar(invitarRef.current)}
          onInvitado={() => {
            cerrar(invitarRef.current);
            setAviso(AVISO_INVITACION);
            releer();
          }}
        />
      )}
      {dialogo?.tipo === "rol" && (
        <ModalCambiarRol
          usuario={dialogo.usuario}
          empresa={empresa}
          rolesAsignables={roles}
          onCerrar={() => cerrar(dialogo.disparador)}
          onCambiado={(fila) => {
            actualizarFila(fila);
            cerrar(dialogo.disparador);
            setAviso(`${fila.nombre} ahora es ${fila.rol_etiqueta}.`);
          }}
        />
      )}
      {dialogo?.tipo === "estado" && (
        <DialogoConfirmarEstado
          usuario={dialogo.usuario}
          accion={dialogo.accion}
          empresa={empresa}
          onCerrar={() => cerrar(dialogo.disparador)}
          onHecho={(fila, texto) => {
            if (fila) actualizarFila(fila);
            else releer();
            cerrar(dialogo.disparador);
            setAviso(texto);
          }}
        />
      )}
    </>
  );
}
