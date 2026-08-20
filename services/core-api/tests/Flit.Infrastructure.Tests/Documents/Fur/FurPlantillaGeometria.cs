using PdfSharpCore.Pdf.Content;
using PdfSharpCore.Pdf.Content.Objects;
using PdfSharpCore.Pdf.IO;

namespace Flit.Infrastructure.Tests.Documents.Fur;

/// <summary>Segmento recto de la plantilla, ya en coordenadas TOP-LEFT (las del manifiesto).</summary>
public readonly record struct Segmento(double Fijo, double Desde, double Hasta);

/// <summary>Celda de una rejilla impresa, en coordenadas top-left.</summary>
public readonly record struct Celda(double X0, double Y0, double X1, double Y1)
{
    public bool Contiene(double x, double y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;

    public double MargenMinimo(double bx0, double by0, double bx1, double by1) =>
        Math.Min(Math.Min(bx0 - X0, X1 - bx1), Math.Min(by0 - Y0, Y1 - by1));

    public override string ToString() =>
        FormattableString.Invariant($"[{X0:0.#},{Y0:0.#} → {X1:0.#},{Y1:0.#}]");
}

/// <summary>
/// Lee la GEOMETRÍA IMPRESA de una plantilla en blanco del FUR: los trazos rectos del formulario
/// oficial, que son los que delimitan las casillas donde el gestor marca.
///
/// <para><b>Por qué existe.</b> El FUR se estampa por coordenadas sobre un PDF preimpreso. Una
/// coordenada equivocada no lanza excepción ni deja log: produce un formulario bien formado y
/// funcionalmente falso. La guardia de <c>FurManifestGuardTests</c> compara el manifiesto contra una
/// línea base CONGELADA, que se regenera a mano; si alguien recalibra mal y regenera, el error queda
/// fijado como esperado —fue exactamente lo que ocurrió con las casillas de prenda (HU #11640)—.
/// Esta clase permite verificar contra el DOCUMENTO OFICIAL en vez de contra una foto de nosotros
/// mismos: las líneas salen del content stream del propio blank, en cada ejecución.</para>
///
/// <para>No añade dependencias: usa el lector de contenido de PdfSharpCore, que ya está en el
/// proyecto para generar el FUR.</para>
/// </summary>
public static class FurPlantillaGeometria
{
    /// <summary>Tolerancia para considerar un trazo horizontal/vertical y para agrupar coordenadas.</summary>
    private const double Epsilon = 0.6;

    public static string RutaPlantilla(string archivo) =>
        Path.Combine(AppContext.BaseDirectory, "Documents", "Fur", "Templates", archivo);

    /// <summary>
    /// Trazos rectos de la página 1, separados en horizontales y verticales y convertidos a
    /// coordenadas top-left (el content stream de un PDF usa origen abajo-izquierda).
    /// </summary>
    public static (List<Segmento> Horizontales, List<Segmento> Verticales) Trazos(string archivo)
    {
        using var doc = PdfReader.Open(RutaPlantilla(archivo), PdfDocumentOpenMode.ReadOnly);
        var pagina = doc.Pages[0];
        var alto = pagina.Height.Point;
        var contenido = ContentReader.ReadContent(pagina);

        var horizontales = new List<Segmento>();
        var verticales = new List<Segmento>();

        double cx = 0, cy = 0;      // punto actual del trazado
        var hayPunto = false;

        for (var i = 0; i < contenido.Count; i++)
        {
            if (contenido[i] is not COperator op) continue;

            switch (op.OpCode.Name)
            {
                case "m" when op.Operands.Count >= 2:
                    cx = Num(op.Operands[0]);
                    cy = alto - Num(op.Operands[1]);   // a top-left
                    hayPunto = true;
                    break;

                case "l" when hayPunto && op.Operands.Count >= 2:
                {
                    var nx = Num(op.Operands[0]);
                    var ny = alto - Num(op.Operands[1]);
                    Acumular(horizontales, verticales, cx, cy, nx, ny);
                    cx = nx;
                    cy = ny;
                    break;
                }

                case "re" when op.Operands.Count >= 4:
                {
                    // Rectángulo: x, y, ancho, alto (con y del borde INFERIOR en espacio PDF).
                    var x = Num(op.Operands[0]);
                    var w = Num(op.Operands[2]);
                    var h = Num(op.Operands[3]);
                    var yInferior = Num(op.Operands[1]);
                    var y0 = alto - (yInferior + h);   // borde superior en top-left
                    var y1 = alto - yInferior;         // borde inferior en top-left
                    horizontales.Add(new Segmento(y0, x, x + w));
                    horizontales.Add(new Segmento(y1, x, x + w));
                    verticales.Add(new Segmento(x, y0, y1));
                    verticales.Add(new Segmento(x + w, y0, y1));
                    hayPunto = false;
                    break;
                }
            }
        }

        return (horizontales, verticales);
    }

    private static void Acumular(
        List<Segmento> horizontales, List<Segmento> verticales,
        double x0, double y0, double x1, double y1)
    {
        if (Math.Abs(y1 - y0) < Epsilon && Math.Abs(x1 - x0) >= Epsilon)
            horizontales.Add(new Segmento((y0 + y1) / 2, Math.Min(x0, x1), Math.Max(x0, x1)));
        else if (Math.Abs(x1 - x0) < Epsilon && Math.Abs(y1 - y0) >= Epsilon)
            verticales.Add(new Segmento((x0 + x1) / 2, Math.Min(y0, y1), Math.Max(y0, y1)));
    }

    private static double Num(CObject o) => o switch
    {
        CReal r => r.Value,
        CInteger n => n.Value,
        _ => throw new InvalidOperationException($"operando no numérico: {o.GetType().Name}"),
    };

    /// <summary>
    /// Celda impresa que ENCIERRA el rectángulo dado: los trazos más próximos por cada lado que
    /// cruzan el rectángulo. Devuelve null si el rectángulo no está encerrado por los cuatro lados
    /// (p. ej. una zona del formulario sin recuadrar).
    /// </summary>
    public static Celda? CeldaQueEncierra(
        (List<Segmento> Horizontales, List<Segmento> Verticales) trazos,
        double x0, double y0, double x1, double y1)
    {
        var cx = (x0 + x1) / 2;
        var cy = (y0 + y1) / 2;

        double? arriba = trazos.Horizontales
            .Where(s => s.Fijo <= y0 + Epsilon && s.Desde - Epsilon <= cx && cx <= s.Hasta + Epsilon)
            .Select(s => (double?)s.Fijo).DefaultIfEmpty(null).Max();

        double? abajo = trazos.Horizontales
            .Where(s => s.Fijo >= y1 - Epsilon && s.Desde - Epsilon <= cx && cx <= s.Hasta + Epsilon)
            .Select(s => (double?)s.Fijo).DefaultIfEmpty(null).Min();

        double? izquierda = trazos.Verticales
            .Where(s => s.Fijo <= x0 + Epsilon && s.Desde - Epsilon <= cy && cy <= s.Hasta + Epsilon)
            .Select(s => (double?)s.Fijo).DefaultIfEmpty(null).Max();

        double? derecha = trazos.Verticales
            .Where(s => s.Fijo >= x1 - Epsilon && s.Desde - Epsilon <= cy && cy <= s.Hasta + Epsilon)
            .Select(s => (double?)s.Fijo).DefaultIfEmpty(null).Min();

        if (arriba is null || abajo is null || izquierda is null || derecha is null) return null;
        return new Celda(izquierda.Value, arriba.Value, derecha.Value, abajo.Value);
    }
}
