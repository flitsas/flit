using System.Buffers.Binary;
using System.IO.Compression;
using BitMiracle.LibTiff.Classic;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace Flit.Comparendos.Api.Infraestructura.Imagenes;

/// <summary>Resultado de convertir un TIFF: PNG si tiene una página, PDF (una página por imagen) si tiene varias.</summary>
public sealed record TiffConvertido(byte[] Contenido, string ContentType, string Extension, int Paginas);

/// <summary>
/// Los TIFF no los acepta el LLM ni los muestra el navegador (matriz GDC, brecha alta del OCR): se convierten al subirlos
/// y el original se guarda como evidencia. Lee con LibTiff.Net (BSD) y escribe el PNG a mano (RGBA de 8 bits, zlib).
/// </summary>
public static class ConversorTiff
{
    /// <summary>Null si el archivo no es un TIFF legible.</summary>
    public static TiffConvertido? Convertir(byte[] tiff)
    {
        ArgumentNullException.ThrowIfNull(tiff);
        var paginas = new List<byte[]>();
        try
        {
            using var lector = Tiff.ClientOpen("comparendo.tif", "r", new MemoryStream(tiff), new TiffStream());
            if (lector is null)
                return null;
            short directorio = 0;
            do
            {
                lector.SetDirectory(directorio);
                var ancho = lector.GetField(TiffTag.IMAGEWIDTH)?[0].ToInt() ?? 0;
                var alto = lector.GetField(TiffTag.IMAGELENGTH)?[0].ToInt() ?? 0;
                if (ancho <= 0 || alto <= 0 || (long)ancho * alto > 60_000_000)
                    return null;
                var raster = new int[ancho * alto];
                if (!lector.ReadRGBAImageOriented(ancho, alto, raster, Orientation.TOPLEFT))
                    return null;
                paginas.Add(Png(ancho, alto, raster));
                directorio++;
            }
            while (lector.ReadDirectory() && paginas.Count < 50);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or NotSupportedException or IndexOutOfRangeException)
        {
            return null;
        }

        if (paginas.Count == 0)
            return null;
        if (paginas.Count == 1)
            return new TiffConvertido(paginas[0], "image/png", ".png", 1);

        var pdf = Document.Create(doc =>
        {
            foreach (var png in paginas)
            {
                doc.Page(p =>
                {
                    p.Size(PageSizes.Letter);
                    p.Margin(0);
                    p.Content().Image(png).FitArea();
                });
            }
        }).GeneratePdf();
        return new TiffConvertido(pdf, "application/pdf", ".pdf", paginas.Count);
    }

    /// <summary>PNG RGBA 8 bits desde el raster ABGR empaquetado de LibTiff.</summary>
    public static byte[] Png(int ancho, int alto, int[] raster)
    {
        ArgumentNullException.ThrowIfNull(raster);
        using var datos = new MemoryStream();
        using (var z = new ZLibStream(datos, CompressionLevel.Optimal, leaveOpen: true))
        {
            var fila = new byte[1 + (ancho * 4)];
            for (var y = 0; y < alto; y++)
            {
                fila[0] = 0; // sin filtro
                for (var x = 0; x < ancho; x++)
                {
                    var p = raster[(y * ancho) + x];
                    var o = 1 + (x * 4);
                    (fila[o], fila[o + 1], fila[o + 2], fila[o + 3]) = ((byte)(p & 0xff), (byte)((p >> 8) & 0xff), (byte)((p >> 16) & 0xff), (byte)((p >> 24) & 0xff));
                }

                z.Write(fila);
            }
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, ancho);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), alto);
        (ihdr[8], ihdr[9], ihdr[10], ihdr[11], ihdr[12]) = (8, 6, 0, 0, 0); // 8 bits, RGBA
        Bloque(png, "IHDR", ihdr);
        Bloque(png, "IDAT", datos.ToArray());
        Bloque(png, "IEND", []);
        return png.ToArray();
    }

    private static void Bloque(Stream s, string tipo, byte[] datos)
    {
        Span<byte> cuatro = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(cuatro, datos.Length);
        s.Write(cuatro);
        var cuerpo = new byte[4 + datos.Length];
        System.Text.Encoding.ASCII.GetBytes(tipo, cuerpo);
        datos.CopyTo(cuerpo, 4);
        s.Write(cuerpo);
        BinaryPrimitives.WriteUInt32BigEndian(cuatro, Crc32(cuerpo));
        s.Write(cuatro);
    }

    private static readonly uint[] Tabla = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(byte[] datos)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in datos)
            c = Tabla[(c ^ b) & 0xff] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
