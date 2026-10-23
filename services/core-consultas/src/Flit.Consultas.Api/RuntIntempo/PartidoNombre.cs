using System.Text.RegularExpressions;

namespace Flit.Consultas.Api.RuntIntempo;

/// <summary>Nombre partido de §6.2 (<c>{ firstName, secondName, firstLastName, secondLastName }</c>).</summary>
internal readonly record struct NombrePartido(string FirstName, string SecondName, string FirstLastName, string SecondLastName);

/// <summary>
/// Partido de nombre (HU #13874, especificación de migración §6.2). Puerto literal de <c>getSeparateNameDriver</c>
/// (BackCrudTransfer_master <c>src/infrastructure/utils/string-conversion/separateNameDriver.ts</c>, 163 líneas) con las
/// listas <c>namesList</c>, <c>preffix</c> y <c>compoundNames</c> de <c>src/domain/models/nameBaseModel.ts</c> (nombres de
/// pila comunes, no datos personales). Coincide con el heredado salvo dos defectos que se corrigen por decisión del
/// usuario (2026-10-23), porque producían nombres partidos erróneos en owner/lessee: (1) 4 palabras con prefijos en la
/// 2.ª y 3.ª dejaban firstName vacío; (2) la última rama de 6 o más palabras anteponía «0» al segundo apellido. Ver
/// los comentarios «Desviación». Puro y sin estado; no registra nada.
/// </summary>
internal static partial class PartidoNombre
{
    // nameBaseModel.ts: namesList.
    private static readonly HashSet<string> Nombres = new(StringComparer.Ordinal)
    {
        "ADOLFO", "ADRIANA", "ALBERTO", "ALEJANDRA", "ALEJANDRO", "ALEXANDER", "ALEXIS", "ALFONSO", "ALONSO", "ALVEIRO",
        "AMPARO", "ANDREA", "ANDRES", "ANGEL", "BERNARDO", "BIBIANA", "CAMILA", "CAMILO", "CARDONA", "CARLOS", "CAROLINA",
        "CATALINA", "CATHERINE", "CECILIA", "CELENI", "CONCEPCION", "CONSTANZA", "CRISTINA", "DAIANA", "DARIO", "DAVID",
        "EDITH", "EDUARDO", "ELENA", "ELIANA", "ELIZABETH", "ENRIQUE", "ERNESTO", "ESTEBAN", "ESTEFANIA", "ESTELA", "ESTELLA",
        "ESTER", "EUGENIA", "FELIPE", "FERNANDA", "FERNANDO", "FRANCISCO", "FREDY", "GABRIEL", "GISELA", "GONZALO", "HENRY",
        "HERNAN", "HUMBERTO", "INES", "ISABEL", "JAVIER", "JESUS", "JIMENA", "JOHANA", "JOSE", "JULIETH", "KARINA", "KATERINE",
        "KATHERINE", "LENIS", "LEON", "LEONIDAS", "LILIANA", "LUCIA", "LUZ", "MANUEL", "MARCELA", "MARIA", "MARIO", "MAURICIO",
        "MELISA", "MELISSA", "MILENA", "NELLY", "NIDIA", "PABLO", "PAOLA", "PATRICIA", "ROMAN", "RUTH", "SANTIAGO", "SIERRA",
        "SILVANA", "SOFIA", "STELLA", "TATIANA", "VALERIA", "VIVIANA", "WILLIAM", "XIMENA", "YANET", "YANETH", "YISETH",
        "YOLANDA", "YULIANA", "YULIET", "YULIETH",
    };

    // nameBaseModel.ts: preffix.
    private static readonly HashSet<string> Prefijos = new(StringComparer.Ordinal) { "DE", "DEL", "LOS", "LA" };

    // nameBaseModel.ts: compoundNames.
    private static readonly HashSet<string> Compuestos = new(StringComparer.Ordinal)
    {
        "JESUS", "ROSARIO", "DIOS", "REFUGIO", "CARMEN", "MAR", "SOL", "PAZ", "ANGELES",
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    /// <summary>Parte el nombre completo. Nulo o vacío devuelve todo vacío (el original lanzaría con nulo).</summary>
    public static NombrePartido Partir(string? nombreCompleto)
    {
        // const cadena = fullName.trim().toUpperCase(); cadena.replace(/\s+/g, ' ').split(' ')
        var names = Espacios().Replace((nombreCompleto ?? string.Empty).Trim().ToUpperInvariant(), " ").Split(' ');

        // names[i] fuera de rango es undefined en JS: includes(undefined) es false.
        string? N(int i) => i < names.Length ? names[i] : null;
        bool Pre(int i) => N(i) is { } s && Prefijos.Contains(s);
        bool Nom(int i) => N(i) is { } s && Nombres.Contains(s);
        bool Com(int i) => N(i) is { } s && Compuestos.Contains(s);

        string firstName = "", secondName = "", firstLastName = "", secondLastName = "";

        switch (names.Length)
        {
            case 2:
                firstName = names[0];
                firstLastName = names[1];
                break;
            case 3:
                if (Pre(1))
                {
                    firstName = names[0];
                    firstLastName = names[1] + " " + names[2];
                }
                else if (Nom(1))
                {
                    firstName = names[0];
                    secondName = names[1];
                    firstLastName = names[2];
                }
                else
                {
                    firstName = names[0];
                    firstLastName = names[1];
                    secondLastName = names[2];
                }

                break;
            case 4:
                if (!Pre(0) && !Pre(1) && !Pre(2) && !Pre(3))
                {
                    firstName = names[0];
                    secondName = names[1];
                    firstLastName = names[2];
                    secondLastName = names[3];
                }
                else if (Pre(1) && Pre(2))
                {
                    // Desviación 1 (decisión del usuario 2026-10-23): el original asigna names[0] a firstLastName y
                    // luego lo pisa, con lo que firstName quedaba vacío. Aquí names[0] es el nombre.
                    firstName = names[0];
                    firstLastName = names[1] + " " + names[2] + " " + names[3];
                }
                else if (Pre(2) && !Pre(1))
                {
                    firstName = names[0];
                    secondName = names[1];
                    firstLastName = names[2] + " " + names[3];
                }
                else if (Pre(1) && Com(2))
                {
                    firstName = names[0];
                    secondName = names[1] + " " + names[2];
                    firstLastName = names[3];
                }
                else if (Pre(1) && !Com(2))
                {
                    firstName = names[0];
                    firstLastName = names[1] + " " + names[2];
                    secondLastName = names[3];
                }

                break;
            case 5:
                if (Pre(1) && Pre(2) && Com(3))
                {
                    firstName = names[0];
                    secondName = names[1] + " " + names[2] + " " + names[3];
                    firstLastName = names[4];
                }
                else if (Pre(1) && Pre(2) && !Com(3))
                {
                    firstName = names[0];
                    firstLastName = names[1] + " " + names[2] + " " + names[3];
                    secondLastName = names[4];
                }
                else if (!Pre(1) && Pre(2) && Pre(3))
                {
                    firstName = names[0];
                    secondName = names[1];
                    firstLastName = names[2] + " " + names[3] + " " + names[4];
                }
                else if (Pre(1) && Com(2) && !Pre(3))
                {
                    firstName = names[0];
                    secondName = names[1] + " " + names[2];
                    firstLastName = names[3];
                    secondLastName = names[4];
                }
                else if (!Pre(1) && Com(2) && Pre(3))
                {
                    firstName = names[0];
                    secondName = names[1] + " " + names[2];
                    firstLastName = names[3];
                    secondLastName = names[4];
                }
                else if (Pre(1) && Pre(2) && !Com(3))
                {
                    // Inalcanzable en el original (misma condición que la segunda rama); se conserva por fidelidad.
                    firstName = names[0];
                    firstLastName = names[1] + " " + names[2] + " " + names[3];
                    secondLastName = names[4];
                }
                else if (!Com(1) && Pre(2) && Pre(3))
                {
                    firstName = names[0];
                    firstLastName = names[1];
                    secondLastName = names[2] + " " + names[3] + " " + names[4];
                }
                else if (Pre(1) && Pre(3) && !Com(2))
                {
                    firstName = names[0];
                    firstLastName = names[1] + " " + names[2];
                    secondLastName = names[3] + " " + names[4];
                }
                else if (Pre(1) && Pre(3) && Com(2))
                {
                    firstName = names[0];
                    secondName = names[1] + " " + names[2];
                    firstLastName = names[3] + " " + names[4];
                }

                break;
            default:
                // 0/1 palabra o 6 y más. lastPosition = names[5..] unidos por espacio.
                var lastPosition = names.Length > 5 ? string.Join(' ', names[5..]) : string.Empty;

                if (Pre(1) && Pre(2) && Com(3))
                {
                    firstName = names[0];
                    secondName = names[1] + " " + names[2] + " " + names[3];
                    firstLastName = names[4];
                    secondLastName = lastPosition;
                }
                else if (Pre(1) && Pre(2) && !Com(3))
                {
                    firstName = names[0];
                    firstLastName = names[1] + " " + names[2] + " " + names[3];
                    secondLastName = names[4] + " " + lastPosition;
                }
                else if (!Pre(1) && Pre(2) && Pre(3))
                {
                    firstName = names[0];
                    secondName = names[1];
                    firstLastName = names[2] + " " + names[3] + " " + names[4];
                    secondLastName = lastPosition;
                }
                else if (Pre(1) && Com(2) && !Pre(3))
                {
                    firstName = names[0];
                    secondName = names[1] + " " + names[2];
                    firstLastName = names[3];
                    secondLastName = names[4] + " " + lastPosition;
                }
                else if (Pre(1) && Pre(2) && !Com(3))
                {
                    // Inalcanzable en el original (misma condición que la segunda rama); se conserva por fidelidad.
                    firstName = names[0];
                    firstLastName = names[1] + " " + names[2] + " " + names[3];
                    secondLastName = names[4] + " " + lastPosition;
                }
                else if (!Com(1) && Pre(2) && Pre(3))
                {
                    firstName = names[0];
                    firstLastName = names[1];
                    secondLastName = names[2] + " " + names[3] + " " + names[4] + " " + lastPosition;
                }
                else if (Pre(1) && Pre(3) && !Com(2))
                {
                    firstName = names[0];
                    firstLastName = names[1] + " " + names[2];
                    secondLastName = names[3] + " " + names[4] + " " + lastPosition;
                }
                else if (Pre(1) && Pre(3) && Com(2))
                {
                    firstName = names[0];
                    secondName = names[1] + " " + names[2];
                    firstLastName = names[3] + " " + names[4];
                    // Desviación 2 (decisión del usuario 2026-10-23): el original hace `secondLastName = + ' ' + lastPosition`;
                    // el «+» unario vuelve 0 el espacio y JavaScript antepone «0». Aquí sale solo lastPosition.
                    secondLastName = lastPosition;
                }

                break;
        }

        return new NombrePartido(firstName, secondName, firstLastName, secondLastName);
    }
}
