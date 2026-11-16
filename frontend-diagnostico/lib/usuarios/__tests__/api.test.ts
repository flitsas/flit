import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api";
import {
  cambiarRolUsuario,
  invitarUsuario,
  listarEmpresasUsuarios,
  listarUsuarios,
  reactivarUsuario,
  suspenderUsuario,
  traducirErrorUsuarios,
} from "@/lib/usuarios/api";
import { TAMANO_POR_DEFECTO } from "@/lib/usuarios/tipos";

// Uso de ejemplo: listarUsuarios(null, filtros) llama a GET /usuarios (compañía del token); listarUsuarios(empresa, …) al
// gemelo del backoffice /backoffice/empresas/{empresa}/usuarios (solo SuperAdmin). invitarUsuario manda solo
// { correo, nombre, rol } (SP-10) y la respuesta es el 202 uniforme (SP-15). Contrato: contrato-api.md §3.3.

const EMPRESA = "00000000-0000-4000-8000-0000000000e1";
const ID = "00000000-0000-4000-8000-000000000001";

const fila = (extra: Record<string, unknown> = {}) => ({
  id: ID,
  tipo: "USUARIO",
  nombre: "Usuario Uno",
  correo: "usuario.uno@ejemplo.com",
  rol: "usuario_diagnostico",
  rol_etiqueta: "Usuario de Diagnóstico",
  estado: "ACTIVO",
  acciones: ["CAMBIAR_ROL", "SUSPENDER"],
  motivo_sin_acciones: null,
  ...extra,
});

const lista = (extra: Record<string, unknown> = {}) => ({
  pagina: 1,
  tamano: 20,
  total: 1,
  roles_asignables: ["usuario_diagnostico"],
  usuarios: [fila()],
  ...extra,
});

function simular(respuesta: Response | (() => Response)) {
  const fetchMock = vi.fn(async () => (typeof respuesta === "function" ? respuesta() : respuesta.clone()));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}
const llamada = (f: ReturnType<typeof simular>, i = 0) => {
  const [url, init] = f.mock.calls[i] as unknown as [string, RequestInit];
  return { url, metodo: init?.method ?? "GET", cuerpo: init?.body === undefined ? undefined : JSON.parse(String(init.body)) };
};

const FILTROS = { busqueda: "", rol: null, estado: null, pagina: 1, tamano: 20 } as const;

afterEach(() => vi.unstubAllGlobals());

describe("HU14588 lib/usuarios — rutas por grupo (contrato §3.2/§3.3)", () => {
  it("sin empresa usa /usuarios (tenant del token) y nunca manda `empresa` en la query", async () => {
    const f = simular(Response.json(lista()));
    await listarUsuarios(null, FILTROS);
    const { url } = llamada(f);
    expect(url.startsWith("/api/v1/diagnostico/usuarios?")).toBe(true);
    expect(new URL(url, "http://x").searchParams.has("empresa")).toBe(false);
  });

  it("con empresa usa /backoffice/empresas/{empresa}/usuarios", async () => {
    const f = simular(Response.json(lista()));
    await listarUsuarios(EMPRESA, FILTROS);
    expect(llamada(f).url.startsWith(`/api/v1/diagnostico/backoffice/empresas/${EMPRESA}/usuarios?`)).toBe(true);
  });

  it("busqueda recortada, rol y estado en explode, pagina y tamano", async () => {
    const f = simular(Response.json(lista()));
    await listarUsuarios(null, { busqueda: "  uno ", rol: "admin_diagnostico", estado: "SUSPENDIDO", pagina: 2, tamano: 10 });
    const q = new URL(llamada(f).url, "http://x").searchParams;
    expect(q.get("busqueda")).toBe("uno");
    expect(q.getAll("rol")).toEqual(["admin_diagnostico"]);
    expect(q.getAll("estado")).toEqual(["SUSPENDIDO"]);
    expect(q.get("pagina")).toBe("2");
    expect(q.get("tamano")).toBe("10");
  });

  it("sin filtros no manda busqueda, rol ni estado", async () => {
    const f = simular(Response.json(lista()));
    await listarUsuarios(null, FILTROS);
    const q = new URL(llamada(f).url, "http://x").searchParams;
    expect(q.has("busqueda") || q.has("rol") || q.has("estado")).toBe(false);
  });
});

describe("HU14588 lib/usuarios — contrato de la respuesta (SP-10, SP-18)", () => {
  it("devuelve la lista con sus campos", async () => {
    simular(Response.json(lista()));
    const r = await listarUsuarios(null, FILTROS);
    expect(r).toMatchObject({ pagina: 1, tamano: 20, total: 1, roles_asignables: ["usuario_diagnostico"] });
    expect(r.usuarios[0]).toMatchObject({ id: ID, nombre: "Usuario Uno", estado: "ACTIVO", acciones: ["CAMBIAR_ROL", "SUSPENDER"] });
  });

  it("descarta roles asignables y acciones fuera de la lista blanca", async () => {
    simular(
      Response.json(
        lista({ roles_asignables: ["usuario_diagnostico", "AdminCompany", "SuperAdmin"], usuarios: [fila({ acciones: ["SUSPENDER", "ELIMINAR"] })] }),
      ),
    );
    const r = await listarUsuarios(null, FILTROS);
    expect(r.roles_asignables).toEqual(["usuario_diagnostico"]);
    expect(r.usuarios[0].acciones).toEqual(["SUSPENDER"]);
  });

  it("una fila de tipo INVITACION nunca trae acciones aunque el cuerpo las mande (H-U2)", async () => {
    simular(
      Response.json(lista({ usuarios: [fila({ tipo: "INVITACION", estado: "INVITADO", acciones: ["SUSPENDER"], motivo_sin_acciones: "INVITACION" })] })),
    );
    const r = await listarUsuarios(null, FILTROS);
    expect(r.usuarios[0].acciones).toEqual([]);
  });

  it("un cuerpo sin arreglo `usuarios` es un contrato roto: lanza, no devuelve vacío", async () => {
    simular(Response.json({ pagina: 1, tamano: 20, total: 0 }));
    await expect(listarUsuarios(null, FILTROS)).rejects.toThrow();
  });
});

describe("HU14588 lib/usuarios — escrituras", () => {
  it("invitar: POST /usuarios/invitaciones solo con correo, nombre y código de rol (SP-10)", async () => {
    const f = simular(new Response(JSON.stringify({ resultado: "ACEPTADA", mensaje: "x" }), { status: 202 }));
    await invitarUsuario(null, { correo: " persona@ejemplo.com ", nombre: " Persona Invitada ", rol: "usuario_diagnostico", rol_id: "x" } as never);
    const { url, metodo, cuerpo } = llamada(f);
    expect(url).toBe("/api/v1/diagnostico/usuarios/invitaciones");
    expect(metodo).toBe("POST");
    expect(cuerpo).toEqual({ correo: "persona@ejemplo.com", nombre: "Persona Invitada", rol: "usuario_diagnostico" });
  });

  it("invitar en el backoffice va al grupo de la empresa", async () => {
    const f = simular(new Response(JSON.stringify({ resultado: "ACEPTADA" }), { status: 202 }));
    await invitarUsuario(EMPRESA, { correo: "a@ejemplo.com", nombre: "A", rol: "admin_diagnostico" });
    expect(llamada(f).url).toBe(`/api/v1/diagnostico/backoffice/empresas/${EMPRESA}/usuarios/invitaciones`);
  });

  it("cambiar rol: PUT /usuarios/{usuario}/rol con { rol } y devuelve la fila", async () => {
    const f = simular(Response.json(fila({ rol: "admin_diagnostico" })));
    const r = await cambiarRolUsuario(null, ID, "admin_diagnostico");
    expect(llamada(f)).toMatchObject({ url: `/api/v1/diagnostico/usuarios/${ID}/rol`, metodo: "PUT", cuerpo: { rol: "admin_diagnostico" } });
    expect(r.rol).toBe("admin_diagnostico");
  });

  it("suspender es POST y reactivar es DELETE sobre /usuarios/{usuario}/suspension, sin cuerpo", async () => {
    const f = simular(() => Response.json(fila({ estado: "SUSPENDIDO", acciones: ["REACTIVAR"] })));
    await suspenderUsuario(EMPRESA, ID);
    await reactivarUsuario(EMPRESA, ID);
    expect(llamada(f, 0)).toMatchObject({
      url: `/api/v1/diagnostico/backoffice/empresas/${EMPRESA}/usuarios/${ID}/suspension`,
      metodo: "POST",
      cuerpo: undefined,
    });
    expect(llamada(f, 1)).toMatchObject({ metodo: "DELETE", cuerpo: undefined });
  });
});

describe("HU14588 lib/usuarios — compañías del SuperAdmin (contrato §3.3)", () => {
  it("GET /backoffice/usuarios/empresas con busqueda, usuarios, pagina y tamano; sin filtro de estado (H-U3)", async () => {
    const f = simular(
      Response.json({
        pagina: 1,
        tamano: 20,
        total: 1,
        empresas: [{ id: EMPRESA, nombre: "Compañía Demo", nit: "900000001", estado: "ACTIVA", usuarios_diagnostico: 4, administradores_diagnostico: 1 }],
      }),
    );
    const r = await listarEmpresasUsuarios({ busqueda: "demo", usuarios: "CON", pagina: 1, tamano: 20 });
    const { url } = llamada(f);
    expect(url.startsWith("/api/v1/diagnostico/backoffice/usuarios/empresas?")).toBe(true);
    const q = new URL(url, "http://x").searchParams;
    expect(q.get("busqueda")).toBe("demo");
    expect(q.get("usuarios")).toBe("CON");
    expect(q.has("estado")).toBe(false);
    expect(r.empresas[0]).toMatchObject({ id: EMPRESA, nombre: "Compañía Demo", usuarios_diagnostico: 4, administradores_diagnostico: 1 });
  });

  it("un cuerpo sin arreglo `empresas` lanza", async () => {
    simular(Response.json({ total: 0 }));
    await expect(listarEmpresasUsuarios({ busqueda: "", usuarios: null, pagina: 1, tamano: 20 })).rejects.toThrow();
  });
});

describe("HU14588 lib/usuarios — textos fijos de error (AP-9)", () => {
  it("traduce por code y nunca devuelve el mensaje del servidor", () => {
    const e = new ApiError(409, "texto del servidor", "ULTIMO_ADMINISTRADOR");
    const texto = traducirErrorUsuarios(e, "respaldo");
    expect(texto).not.toBe("texto del servidor");
    expect(texto).toMatch(/al menos un administrador/i);
  });

  it("503 de la plataforma tiene su propio texto; lo desconocido cae en el respaldo", () => {
    expect(traducirErrorUsuarios(new ApiError(503, "x", "PLATAFORMA_NO_DISPONIBLE"), "respaldo")).not.toBe("respaldo");
    expect(traducirErrorUsuarios(new Error("boom"), "respaldo")).toBe("respaldo");
  });
});

// D-6 (contrato, 2026-11-16): el 200 del backoffice trae la empresa de la cabecera; la paginación es del servidor.
describe("HU14588 lib/usuarios — D-6 empresa de la cabecera y paginación", () => {
  it("la lista del backoffice trae `empresa` { id, nombre, nit }", async () => {
    simular(Response.json(lista({ empresa: { id: EMPRESA, nombre: "Compañía Demo", nit: "900000001" } })));
    const r = await listarUsuarios(EMPRESA, FILTROS);
    expect(r.empresa).toEqual({ id: EMPRESA, nombre: "Compañía Demo", nit: "900000001" });
  });

  it("nit nulo se conserva como null; sin `empresa` (back anterior o ruta del cliente) queda undefined", async () => {
    simular(Response.json(lista({ empresa: { id: EMPRESA, nombre: "Compañía Demo", nit: null } })));
    expect((await listarUsuarios(EMPRESA, FILTROS)).empresa).toEqual({ id: EMPRESA, nombre: "Compañía Demo", nit: null });
    simular(Response.json(lista()));
    expect((await listarUsuarios(null, FILTROS)).empresa).toBeUndefined();
  });

  it("tamano se acota a 1..100 y pagina a >= 1 antes de llamar (contrato §3.3)", async () => {
    const f = simular(Response.json(lista()));
    await listarUsuarios(null, { ...FILTROS, pagina: 0, tamano: 500 });
    await listarEmpresasUsuarios({ busqueda: "", usuarios: null, pagina: -3, tamano: 0 }).catch(() => undefined);
    const q1 = new URL(llamada(f, 0).url, "http://x").searchParams;
    const q2 = new URL(llamada(f, 1).url, "http://x").searchParams;
    expect([q1.get("pagina"), q1.get("tamano")]).toEqual(["1", "100"]);
    expect([q2.get("pagina"), q2.get("tamano")]).toEqual(["1", "1"]);
  });

  it("el tamaño por defecto es 25 (D-6)", () => {
    expect(TAMANO_POR_DEFECTO).toBe(25);
  });
});
