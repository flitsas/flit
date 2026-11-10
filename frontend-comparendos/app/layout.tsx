import type { Metadata } from "next";
import localFont from "next/font/local";
import { BrandProvider } from "@flit/brand/BrandProvider";
import { BrandStyle } from "@flit/brand/BrandStyle";
import { resolveBrand } from "@flit/brand/resolve-brand.server";
import { THEME_INIT_SCRIPT } from "@flit/shell/theme-script";
import "./globals.css";

// Las fuentes viven en el repo: el build no depende de lo que responda Google Fonts.
const poppins = localFont({
  variable: "--font-poppins",
  src: [
    { path: "./fonts/Poppins-400-latin.woff2", weight: "400", style: "normal" },
    { path: "./fonts/Poppins-500-latin.woff2", weight: "500", style: "normal" },
    { path: "./fonts/Poppins-600-latin.woff2", weight: "600", style: "normal" },
    { path: "./fonts/Poppins-700-latin.woff2", weight: "700", style: "normal" },
  ],
});

// Placa colombiana (UI kit «Diseño Inteligente», HU #14475): local, como Poppins.
const barlowCondensed = localFont({
  variable: "--font-barlow-condensed",
  src: "./fonts/BarlowCondensed-700-latin.woff2",
  weight: "700",
  style: "normal",
});

// La marca sale del host de la petición (Marca Blanca, ADR-0060 §D5): en un host FLIT no se llama a nadie; en el
// dominio de una red, título, favicon y colores son los de la red. Se resuelve en el servidor, sin destello.
export async function generateMetadata(): Promise<Metadata> {
  const brand = await resolveBrand();
  return {
    title: `Comparendos · ${brand.platformName}`,
    description: "Gestión de comparendos: ingreso, plazos, conductores, derechos de petición y pagos",
    icons: brand.logoUrl ? { icon: brand.logoUrl } : undefined,
  };
}

export default async function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  const brand = await resolveBrand();

  return (
    <html lang="es" suppressHydrationWarning className={`${poppins.variable} ${barlowCondensed.variable} h-full scroll-smooth`}>
      <head>
        {/* Tema claro/oscuro antes de pintar, sin destello (la preferencia es la misma que en Trámites). */}
        <script dangerouslySetInnerHTML={{ __html: THEME_INIT_SCRIPT }} />
        <BrandStyle brand={brand} />
      </head>
      <body suppressHydrationWarning className="h-full font-sans antialiased">
        <BrandProvider brand={brand}>{children}</BrandProvider>
      </body>
    </html>
  );
}
