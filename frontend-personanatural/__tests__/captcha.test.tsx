import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Captcha, proveedorConfigurado, TOKEN_MODO_DESARROLLO } from "@/components/Captcha";

afterEach(() => {
  vi.unstubAllEnvs();
  delete (window as unknown as { turnstile?: unknown }).turnstile;
  delete (window as unknown as { grecaptcha?: unknown }).grecaptcha;
});

describe("Captcha: proveedor por variable", () => {
  it("sin variable, en desarrollo y pruebas usa el modo dev; en producción no hay proveedor", () => {
    expect(proveedorConfigurado()).toBe("dev");
    vi.stubEnv("NODE_ENV", "production");
    expect(proveedorConfigurado()).toBe("ninguno");
    vi.stubEnv("NEXT_PUBLIC_CAPTCHA_PROVIDER", "turnstile");
    expect(proveedorConfigurado()).toBe("turnstile");
    vi.stubEnv("NEXT_PUBLIC_CAPTCHA_PROVIDER", "otro");
    expect(proveedorConfigurado()).toBe("ninguno");
  });

  it("modo dev: la casilla entrega un token fijo y se desmarca al reiniciar", () => {
    const onToken = vi.fn();
    const { rerender } = render(<Captcha onToken={onToken} proveedor="dev" />);
    const casilla = screen.getByLabelText("Verificación de seguridad");

    fireEvent.click(casilla);
    expect(onToken).toHaveBeenLastCalledWith(TOKEN_MODO_DESARROLLO);
    expect(casilla).toBeChecked();

    rerender(<Captcha onToken={onToken} proveedor="dev" reinicio={1} />);
    expect(casilla).not.toBeChecked();
    expect(onToken).toHaveBeenLastCalledWith(null);
  });

  it("sin proveedor configurado (producción) avisa con role=alert y nunca entrega token", () => {
    const onToken = vi.fn();
    render(<Captcha onToken={onToken} proveedor="ninguno" />);

    expect(screen.getByRole("alert")).toHaveTextContent("La verificación de seguridad no está disponible");
    expect(onToken).not.toHaveBeenCalled();
  });

  it("un proveedor externo sin clave del sitio tampoco entrega token", () => {
    const onToken = vi.fn();
    render(<Captcha onToken={onToken} proveedor="turnstile" siteKey="" />);

    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(document.querySelector('script[src*="challenges.cloudflare.com"]')).toBeNull();
  });

  it("turnstile: carga el script del proveedor, dibuja el widget con la clave y entrega el token", async () => {
    const opciones: Record<string, unknown>[] = [];
    const api = { render: vi.fn((_el: HTMLElement, o: Record<string, unknown>) => (opciones.push(o), "w1")), reset: vi.fn(), remove: vi.fn() };
    (window as unknown as { turnstile: typeof api }).turnstile = api;
    const onToken = vi.fn();

    const { rerender, unmount } = render(<Captcha onToken={onToken} proveedor="turnstile" siteKey="clave-de-prueba" />);
    const script = document.querySelector<HTMLScriptElement>('script[src^="https://challenges.cloudflare.com/turnstile"]');
    expect(script).not.toBeNull();
    act(() => script!.onload?.(new Event("load")));

    await waitFor(() => expect(api.render).toHaveBeenCalledTimes(1));
    expect(opciones[0].sitekey).toBe("clave-de-prueba");
    act(() => (opciones[0].callback as (t: string) => void)("token-123"));
    expect(onToken).toHaveBeenLastCalledWith("token-123");
    act(() => (opciones[0]["expired-callback"] as () => void)());
    expect(onToken).toHaveBeenLastCalledWith(null);

    rerender(<Captcha onToken={onToken} proveedor="turnstile" siteKey="clave-de-prueba" reinicio={1} />);
    expect(api.reset).toHaveBeenCalledWith("w1");
    unmount();
    expect(api.remove).toHaveBeenCalledWith("w1");
  });

  it("si el script del proveedor no carga, avisa con role=alert", async () => {
    render(<Captcha onToken={() => {}} proveedor="recaptcha" siteKey="clave-de-prueba" />);
    const script = document.querySelector<HTMLScriptElement>('script[src^="https://www.google.com/recaptcha"]');

    act(() => script!.onerror?.(new Event("error")));

    expect(await screen.findByRole("alert")).toHaveTextContent("No pudimos cargar la verificación de seguridad");
  });
});
