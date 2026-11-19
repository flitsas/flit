import type { Metadata } from "next";
import localFont from "next/font/local";
import { connection } from "next/server";
import { ConfigPublica } from "@/components/ConfigPublica";
import { portalConfig } from "@/lib/config.server";
import { Providers } from "./providers";
import "./globals.css";

// Tipografía del diseño de referencia (Poppins 300–700). Las fuentes viven en el repo: el build no depende de lo que
// responda Google Fonts.
const poppins = localFont({
  variable: "--font-poppins",
  src: [
    { path: "./fonts/Poppins-300-latin.woff2", weight: "300", style: "normal" },
    { path: "./fonts/Poppins-400-latin.woff2", weight: "400", style: "normal" },
    { path: "./fonts/Poppins-500-latin.woff2", weight: "500", style: "normal" },
    { path: "./fonts/Poppins-600-latin.woff2", weight: "600", style: "normal" },
    { path: "./fonts/Poppins-700-latin.woff2", weight: "700", style: "normal" },
  ],
});

export const metadata: Metadata = {
  title: "FLIT Persona Natural",
  description: "Trámites de tránsito 100% en línea.",
};

// El layout se renderiza por petición (`connection()`): el proveedor del captcha y la clave pública del sitio salen de las
// variables de servidor del ambiente y viajan al navegador en el HTML, no incrustados en el build.
export default async function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  await connection();
  const { captcha } = portalConfig();
  return (
    // suppressHydrationWarning: extensiones del navegador (p. ej. captura de firma) agregan atributos a <html> antes de hidratar.
    <html
      lang="es"
      suppressHydrationWarning
      className={`${poppins.variable} h-full scroll-smooth`}
      style={{ ["--font-sans" as string]: "var(--font-poppins), Poppins, sans-serif" }}
    >
      <body className="h-full font-sans antialiased">
        <ConfigPublica captcha={captcha}>
          <Providers>{children}</Providers>
        </ConfigPublica>
      </body>
    </html>
  );
}
