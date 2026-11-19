// HU #14455 (Feature #14453) — «Usuarios de caja» en el hub del OT: lista con estado (AC1), crear (AC2), desactivar/reactivar
// (AC3), reenviar invitación (AC4) y errores exactos (correo en uso, caja no habilitada). La API es un doble.
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiError } from "@/lib/api/types";
import type { CajaApi, CajaUsuario } from "@/lib/api/admin-ot-portal-caja";
import { OtCajaUsuariosSection, CAJA_USUARIOS_VACIO } from "../OtCajaUsuariosSection";

vi.mock("@/hooks/usePermissions", () => ({ usePermissions: () => ({ isSuperAdmin: false }) }));

const ACTIVO: CajaUsuario = { userId: "u1", invitationId: null, fullName: "Ana Caja", email: "ana@ot.co", status: "active", createdAt: "2026-11-10T15:30:00Z" };
const SUSPENDIDO: CajaUsuario = { userId: "u2", invitationId: null, fullName: "Beto Caja", email: "beto@ot.co", status: "suspended", createdAt: null };
const INVITADO: CajaUsuario = { userId: null, invitationId: "i1", fullName: "Cata Caja", email: "cata@ot.co", status: "invited", createdAt: null };

function api(lista: CajaUsuario[], extra?: Partial<CajaApi>): CajaApi {
  return {
    listarUsuarios: vi.fn().mockResolvedValue(lista),
    invitar: vi.fn().mockResolvedValue({}),
    suspender: vi.fn().mockResolvedValue(undefined),
    reactivar: vi.fn().mockResolvedValue(undefined),
    reenviar: vi.fn().mockResolvedValue(undefined),
    listarCobros: vi.fn(),
    anular: vi.fn(),
    ...extra,
  };
}

const montar = (a: CajaApi) => render(<OtCajaUsuariosSection transitOfficeId="ot-1" api={a} />);

describe("OtCajaUsuariosSection", () => {
  it("AC1: lista a los usuarios con su estado y la fecha DD/MM/YYYY HH:mm", async () => {
    montar(api([ACTIVO, SUSPENDIDO, INVITADO]));
    expect(await screen.findByText("Ana Caja")).toBeInTheDocument();
    expect(screen.getByText("Activo")).toBeInTheDocument();
    expect(screen.getByText("Desactivado")).toBeInTheDocument();
    expect(screen.getByText("Invitación pendiente")).toBeInTheDocument();
    expect(screen.getByText("10/11/2026 10:30")).toBeInTheDocument();
  });

  it("muestra el vacío cuando no hay usuarios", async () => {
    montar(api([]));
    expect(await screen.findByText(CAJA_USUARIOS_VACIO)).toBeInTheDocument();
  });

  it("muestra el error con reintento si falla la carga", async () => {
    const a = api([], { listarUsuarios: vi.fn().mockRejectedValue(new ApiError(500, "x")) });
    montar(a);
    expect(await screen.findByRole("alert")).toBeInTheDocument();
  });

  it("caja apagada (404 sin código): avisa que no está habilitada y no deja crear", async () => {
    const a = api([], { listarUsuarios: vi.fn().mockRejectedValue(new ApiError(404, "x")) });
    montar(a);
    expect(await screen.findByText("La caja no está habilitada.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Crear usuario de caja/ })).toBeDisabled();
  });

  it("AC2: crea el usuario con nombre y correo y recarga la lista", async () => {
    const a = api([ACTIVO]);
    montar(a);
    await screen.findByText("Ana Caja");
    await userEvent.click(screen.getByRole("button", { name: /Crear usuario de caja/ }));
    const dlg = await screen.findByRole("dialog");
    await userEvent.type(within(dlg).getByLabelText("Nombre"), "Dora Caja");
    await userEvent.type(within(dlg).getByLabelText("Correo"), "dora@ot.co");
    await userEvent.click(within(dlg).getByRole("button", { name: /Enviar invitación/ }));
    await waitFor(() => expect(a.invitar).toHaveBeenCalledWith({ fullName: "Dora Caja", email: "dora@ot.co" }));
    expect(await screen.findByText("Invitación enviada.")).toBeInTheDocument();
    expect(a.listarUsuarios).toHaveBeenCalledTimes(2);
  });

  it("AC2: sin nombre ni correo válido no envía y dice qué falta", async () => {
    const a = api([]);
    montar(a);
    await screen.findByText(CAJA_USUARIOS_VACIO);
    await userEvent.click(screen.getByRole("button", { name: /Crear usuario de caja/ }));
    const dlg = await screen.findByRole("dialog");
    await userEvent.type(within(dlg).getByLabelText("Correo"), "mal");
    await userEvent.click(within(dlg).getByRole("button", { name: /Enviar invitación/ }));
    expect(await within(dlg).findByText("Escribe el nombre de la persona.")).toBeInTheDocument();
    expect(within(dlg).getByText("Escribe un correo válido.")).toBeInTheDocument();
    expect(a.invitar).not.toHaveBeenCalled();
  });

  it("AC2: correo en uso muestra el mensaje del servidor y mantiene el modal abierto", async () => {
    const err = new ApiError(409, "x", { code: "EMAIL_ALREADY_IN_USE", message: "El usuario ya se encuentra previamente creado" });
    const a = api([], { invitar: vi.fn().mockRejectedValue(err) });
    montar(a);
    await screen.findByText(CAJA_USUARIOS_VACIO);
    await userEvent.click(screen.getByRole("button", { name: /Crear usuario de caja/ }));
    const dlg = await screen.findByRole("dialog");
    await userEvent.type(within(dlg).getByLabelText("Nombre"), "Ana");
    await userEvent.type(within(dlg).getByLabelText("Correo"), "ana@ot.co");
    await userEvent.click(within(dlg).getByRole("button", { name: /Enviar invitación/ }));
    expect(await within(dlg).findByText("El usuario ya se encuentra previamente creado")).toBeInTheDocument();
  });

  it("AC2: producto caja no habilitado en el organismo", async () => {
    const err = new ApiError(409, "x", { code: "PRODUCT_NOT_ENABLED_FOR_HEAD", message: "detalle técnico" });
    const a = api([], { invitar: vi.fn().mockRejectedValue(err) });
    montar(a);
    await screen.findByText(CAJA_USUARIOS_VACIO);
    await userEvent.click(screen.getByRole("button", { name: /Crear usuario de caja/ }));
    const dlg = await screen.findByRole("dialog");
    await userEvent.type(within(dlg).getByLabelText("Nombre"), "Ana");
    await userEvent.type(within(dlg).getByLabelText("Correo"), "ana@ot.co");
    await userEvent.click(within(dlg).getByRole("button", { name: /Enviar invitación/ }));
    expect(await within(dlg).findByText("La caja no está habilitada para este organismo.")).toBeInTheDocument();
  });

  it("AC3: desactiva a un usuario activo desde «Acciones»", async () => {
    const a = api([ACTIVO]);
    montar(a);
    await userEvent.click(await screen.findByRole("button", { name: "Acciones para Ana Caja" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: /Desactivar a Ana Caja/ }));
    await waitFor(() => expect(a.suspender).toHaveBeenCalledWith("u1"));
    expect(await screen.findByText("Usuario desactivado.")).toBeInTheDocument();
  });

  it("AC3: reactiva a un usuario desactivado", async () => {
    const a = api([SUSPENDIDO]);
    montar(a);
    await userEvent.click(await screen.findByRole("button", { name: "Acciones para Beto Caja" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: /Reactivar a Beto Caja/ }));
    await waitFor(() => expect(a.reactivar).toHaveBeenCalledWith("u2"));
    expect(await screen.findByText("Usuario reactivado.")).toBeInTheDocument();
  });

  it("AC4: reenvía la invitación pendiente", async () => {
    const a = api([INVITADO]);
    montar(a);
    await userEvent.click(await screen.findByRole("button", { name: "Acciones para Cata Caja" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: /Reenviar invitación a Cata Caja/ }));
    await waitFor(() => expect(a.reenviar).toHaveBeenCalledWith("i1"));
    expect(await screen.findByText("Invitación reenviada.")).toBeInTheDocument();
  });

  it("una acción que falla muestra el mensaje del servidor", async () => {
    const err = new ApiError(429, "x", { code: "RESEND_COOLDOWN", message: "Espere un momento antes de reenviar de nuevo la invitación." });
    const a = api([INVITADO], { reenviar: vi.fn().mockRejectedValue(err) });
    montar(a);
    await userEvent.click(await screen.findByRole("button", { name: "Acciones para Cata Caja" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: /Reenviar invitación/ }));
    expect(await screen.findByText("Espere un momento antes de reenviar de nuevo la invitación.")).toBeInTheDocument();
  });
});
