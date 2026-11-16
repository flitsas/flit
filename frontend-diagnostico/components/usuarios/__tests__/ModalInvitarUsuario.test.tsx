import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { ModalInvitarUsuario } from "@/components/usuarios/ModalInvitarUsuario";

// Uso de ejemplo: <ModalInvitarUsuario abierto empresa={null} rolesAsignables={lista.roles_asignables}
//   onCerrar={cerrar} onInvitado={() => { mostrarAviso(); recargar(); }} />
// Campos Nombre completo, Correo electrónico y Rol (solo los de `roles_asignables`, SP-11=a). Manda solo
// { correo, nombre, rol } (SP-10). Los errores van por campo con aria-invalid y aria-describedby; los de la API, dentro del
// diálogo, sin cerrarlo. Datos: placeholders @ejemplo.com.

afterEach(() => vi.unstubAllGlobals());

function montar(props: Partial<Parameters<typeof ModalInvitarUsuario>[0]> = {}) {
  const onCerrar = vi.fn();
  const onInvitado = vi.fn();
  render(
    <ModalInvitarUsuario abierto empresa={null} rolesAsignables={["usuario_diagnostico"]} onCerrar={onCerrar} onInvitado={onInvitado} {...props} />,
  );
  const dialogo = screen.getByRole("dialog", { name: "Invitar usuario" });
  return {
    dialogo,
    onCerrar,
    onInvitado,
    nombre: within(dialogo).getByRole("textbox", { name: "Nombre completo" }),
    correo: within(dialogo).getByRole("textbox", { name: "Correo electrónico" }),
    enviar: within(dialogo).getByRole("button", { name: "Enviar invitación" }),
  };
}

const respuesta202 = () => new Response(JSON.stringify({ resultado: "ACEPTADA", mensaje: "Si el correo es válido, …" }), { status: 202 });

describe("HU14588 ModalInvitarUsuario", () => {
  it("foco inicial en «Nombre completo»", async () => {
    const { nombre } = montar();
    await waitFor(() => expect(nombre).toHaveFocus());
  });

  it("el admin de compañía solo ve «Usuario de Diagnóstico» (roles_asignables, SP-11=a)", () => {
    const { dialogo } = montar({ rolesAsignables: ["usuario_diagnostico"] });
    const radios = within(dialogo).getAllByRole("radio");
    expect(radios).toHaveLength(1);
    expect(within(dialogo).getByRole("radio", { name: /Usuario de Diagnóstico/ })).toBeChecked();
    expect(within(dialogo).queryByRole("radio", { name: /Administrador de Diagnóstico/ })).toBeNull();
  });

  it("el SuperAdmin ve los dos roles de Diagnóstico y ningún otro", () => {
    const { dialogo } = montar({ rolesAsignables: ["usuario_diagnostico", "admin_diagnostico"] });
    const nombres = within(dialogo)
      .getAllByRole("radio")
      .map((r) => r.closest("label")?.textContent ?? "");
    expect(nombres).toHaveLength(2);
    expect(nombres[0]).toMatch(/Usuario de Diagnóstico/);
    expect(nombres[1]).toMatch(/Administrador de Diagnóstico/);
  });

  it("sin datos: no llama a la API y marca los campos con aria-invalid y su descripción", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    const { nombre, correo, enviar } = montar();
    fireEvent.click(enviar);
    await waitFor(() => expect(nombre).toHaveAttribute("aria-invalid", "true"));
    expect(correo).toHaveAttribute("aria-invalid", "true");
    expect(nombre).toHaveAccessibleDescription(/\S/);
    expect(correo).toHaveAccessibleDescription(/\S/);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("correo con forma inválida: error en el campo de correo", async () => {
    vi.stubGlobal("fetch", vi.fn());
    const { nombre, correo, enviar } = montar();
    fireEvent.change(nombre, { target: { value: "Persona Invitada" } });
    fireEvent.change(correo, { target: { value: "no-es-correo" } });
    fireEvent.click(enviar);
    await waitFor(() => expect(correo).toHaveAttribute("aria-invalid", "true"));
    expect(nombre).not.toHaveAttribute("aria-invalid", "true");
  });

  it("envía solo { correo, nombre, rol } y avisa con onInvitado", async () => {
    const fetchMock = vi.fn(async () => respuesta202());
    vi.stubGlobal("fetch", fetchMock);
    const { nombre, correo, enviar, onInvitado } = montar();
    fireEvent.change(nombre, { target: { value: "Persona Invitada" } });
    fireEvent.change(correo, { target: { value: "persona.invitada@ejemplo.com" } });
    fireEvent.click(enviar);
    await waitFor(() => expect(onInvitado).toHaveBeenCalledTimes(1));
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/v1/diagnostico/usuarios/invitaciones");
    expect(JSON.parse(String(init.body))).toEqual({ correo: "persona.invitada@ejemplo.com", nombre: "Persona Invitada", rol: "usuario_diagnostico" });
  });

  it("403 ROL_NO_ASIGNABLE: error dentro del diálogo, sin cerrarlo ni avisar", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => Response.json({ code: "ROL_NO_ASIGNABLE", error: "texto del servidor" }, { status: 403 })));
    const { dialogo, nombre, correo, enviar, onInvitado, onCerrar } = montar({ rolesAsignables: ["usuario_diagnostico", "admin_diagnostico"] });
    fireEvent.change(nombre, { target: { value: "Persona" } });
    fireEvent.change(correo, { target: { value: "persona@ejemplo.com" } });
    fireEvent.click(within(dialogo).getByRole("radio", { name: /Administrador de Diagnóstico/ }));
    fireEvent.click(enviar);
    expect(await within(dialogo).findByText(/Solo un SuperAdmin/)).toBeInTheDocument();
    expect(within(dialogo).queryByText("texto del servidor")).toBeNull();
    expect(onInvitado).not.toHaveBeenCalled();
    expect(onCerrar).not.toHaveBeenCalled();
  });

  it("400 CORREO_INVALIDO de la API queda en el campo de correo", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => Response.json({ code: "CORREO_INVALIDO", campo: "correo" }, { status: 400 })));
    const { nombre, correo, enviar } = montar();
    fireEvent.change(nombre, { target: { value: "Persona" } });
    fireEvent.change(correo, { target: { value: "persona@ejemplo.com" } });
    fireEvent.click(enviar);
    await waitFor(() => expect(correo).toHaveAttribute("aria-invalid", "true"));
  });

  it("429: aviso de demasiadas invitaciones dentro del diálogo", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => Response.json({ code: "DEMASIADAS_SOLICITUDES" }, { status: 429, headers: { "Retry-After": "60" } })));
    const { dialogo, nombre, correo, enviar } = montar();
    fireEvent.change(nombre, { target: { value: "Persona" } });
    fireEvent.change(correo, { target: { value: "persona@ejemplo.com" } });
    fireEvent.click(enviar);
    expect(await within(dialogo).findByText(/muchas invitaciones/i)).toBeInTheDocument();
  });

  it("«Cancelar» llama a onCerrar", () => {
    const { dialogo, onCerrar } = montar();
    fireEvent.click(within(dialogo).getByRole("button", { name: "Cancelar" }));
    expect(onCerrar).toHaveBeenCalled();
  });
});

// Code review HU #14588 (CR-3): el resumen de errores es una sola región role="alert" (sin otra viva adentro).
describe("HU14588 ModalInvitarUsuario — una sola región de alerta (CR-3)", () => {
  it("con errores de validación hay exactamente un role=alert y ninguna región viva anidada", async () => {
    vi.stubGlobal("fetch", vi.fn());
    const { dialogo, enviar } = montar();
    fireEvent.click(enviar);
    const alertas = await within(dialogo).findAllByRole("alert");
    expect(alertas).toHaveLength(1);
    expect(alertas[0].querySelectorAll('[role="alert"], [role="status"], [aria-live]')).toHaveLength(0);
  });
});
