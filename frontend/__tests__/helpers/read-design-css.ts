import { readFileSync } from "node:fs";
import path from "node:path";

const FE_ROOT = path.resolve(__dirname, "..", "..");

/**
 * CSS de diseño que ve la app: los tokens compartidos de @flit/ui (B-02), los estilos del dock de @flit/shell (B-13)
 * y `app/globals.css`, que importa los dos. Las pruebas de contrato de tokens leen este texto en vez de
 * solo `globals.css`, porque `@theme`, `:root` y `.dark` se mudaron al paquete.
 */
export function readDesignCss(): string {
  const tokens = readFileSync(path.join(FE_ROOT, "..", "packages", "ui", "src", "styles", "tokens.css"), "utf8");
  const shell = readFileSync(path.join(FE_ROOT, "..", "packages", "shell", "src", "styles", "shell.css"), "utf8");
  const globals = readFileSync(path.join(FE_ROOT, "app", "globals.css"), "utf8");
  return `${tokens}\n${shell}\n${globals}`;
}
