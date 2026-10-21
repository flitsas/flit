import type { Metadata } from "next";
import { Poppins } from "next/font/google";
import { BrandProvider } from "@flit/brand/BrandProvider";
import { BrandStyle } from "@flit/brand/BrandStyle";
import { resolveBrand } from "@flit/brand/resolve-brand.server";
import { THEME_INIT_SCRIPT } from "@flit/shell/theme-script";
import "./globals.css";

const poppins = Poppins({
  variable: "--font-poppins",
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
});

// La marca sale del host de la petición (Marca Blanca, ADR-0060 §D5): en un host FLIT no se llama a nadie; en el
// dominio de una red, título, favicon y colores son los de la red. Se resuelve en el servidor, sin destello. En un host
// FLIT el favicon es app/icon.svg, el de la suite (Diseño aún no entrega uno propio de Diagnóstico).
export async function generateMetadata(): Promise<Metadata> {
  const brand = await resolveBrand();
  return {
    title: `Diagnóstico · ${brand.platformName}`,
    description: "Diagnóstico vehicular de la flota de tu compañía",
    icons: brand.logoUrl ? { icon: brand.logoUrl } : undefined,
  };
}

export default async function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  const brand = await resolveBrand();

  return (
    <html lang="es" suppressHydrationWarning className={`${poppins.variable} h-full scroll-smooth`}>
      <head>
        {/* Tema claro/oscuro antes de pintar, sin destello (la preferencia es la misma que en Trámites). Es una
            constante del paquete @flit/shell, no contenido de usuario. */}
        <script dangerouslySetInnerHTML={{ __html: THEME_INIT_SCRIPT }} />
        <BrandStyle brand={brand} />
      </head>
      <body suppressHydrationWarning className="h-full font-sans antialiased">
        <BrandProvider brand={brand}>{children}</BrandProvider>
      </body>
    </html>
  );
}
