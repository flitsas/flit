import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { HubLogin } from "../HubLogin";

// A-06 (HU #12991): el login del hub abre la sesión con POST /connect/login y vuelve a donde lo pidieron.
vi.mock("../ParticlesCanvas", () => ({ ParticlesCanvas: () => null }));

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function respond(status: number, body: unknown) {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

async function submit(email = "a@b.co", password = "Clave123") {
  fireEvent.change(screen.getByLabelText("Usuario Corporativo"), { target: { value: email } });
  fireEvent.change(screen.getByLabelText("Contraseña"), { target: { value: password } });
  fireEvent.click(screen.getByRole("button", { name: "Iniciar Sesión" }));
}

describe("HubLogin", () => {
  it("abre la sesión y vuelve a la petición de authorize que pidió el login", async () => {
    const fetchMock = respond(200, { returnUrl: "/connect/authorize?client_id=tramites" });
    const navigate = vi.fn();
    render(<HubLogin returnUrl="/connect/authorize?client_id=tramites" navigate={navigate} />);

    await submit();

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/connect/authorize?client_id=tramites"));
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/connect/login");
    expect(JSON.parse(init.body as string)).toEqual({ email: "a@b.co", password: "Clave123", returnUrl: "/connect/authorize?client_id=tramites" });
  });

  it("sin retorno válido va al inicio del hub", async () => {
    respond(200, { returnUrl: null });
    const navigate = vi.fn();
    render(<HubLogin navigate={navigate} />);

    await submit();

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/"));
  });

  it("credencial incorrecta: el mismo mensaje de siempre, sin navegar", async () => {
    respond(401, { code: "INVALID_CREDENTIALS" });
    const navigate = vi.fn();
    render(<HubLogin navigate={navigate} />);

    await submit();

    expect(await screen.findByRole("alert")).toHaveTextContent("Correo o contraseña incorrectos.");
    expect(navigate).not.toHaveBeenCalled();
  });

  it("usuario de una red con dominio propio: ofrece el enlace a su dominio", async () => {
    respond(403, { error: "NETWORK_DOMAIN_REQUIRED", networkDomain: "app.red.co", loginUrl: "https://app.red.co/login" });
    render(<HubLogin navigate={vi.fn()} />);

    await submit();

    expect(await screen.findByRole("link", { name: "Ir a app.red.co" })).toHaveAttribute("href", "https://app.red.co/login");
  });

  it("cuenta bloqueada y rol desactivado muestran su panel", async () => {
    respond(403, { code: "ACCOUNT_TEMPORARILY_BLOCKED" });
    const { unmount } = render(<HubLogin navigate={vi.fn()} />);
    await submit();
    expect(await screen.findByText(/bloqueada temporalmente/)).toBeInTheDocument();
    unmount();

    respond(403, { code: "ALL_ROLES_INACTIVE" });
    render(<HubLogin navigate={vi.fn()} />);
    await submit();
    expect(await screen.findByText(/rol ha sido desactivado/)).toBeInTheDocument();
  });

  it("sin datos no llama al servidor", async () => {
    const fetchMock = respond(200, {});
    render(<HubLogin navigate={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Iniciar Sesión" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Ingresa tu correo y contraseña.");
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
