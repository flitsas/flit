namespace Flit.Admin.Domain.OtQueries;

/// <summary>
/// Ya existe una consulta con ese nombre para ese usuario en ese organismo.
///
/// <para>Se avisa en vez de renombrar sola («Rechazados (2)»): quien guarda con un nombre repetido
/// casi siempre quería SOBRESCRIBIR la que ya tenía, y crearle una copia silenciosa le deja dos
/// consultas indistinguibles en la lista.</para>
/// </summary>
public sealed class OtSavedQueryNameTakenException : Exception
{
    public OtSavedQueryNameTakenException(string nombre)
        : base($"Ya tiene una consulta guardada llamada «{nombre}».") => Nombre = nombre;

    public OtSavedQueryNameTakenException()
        : base("Ya tiene una consulta guardada con ese nombre.") => Nombre = string.Empty;

    public OtSavedQueryNameTakenException(string message, Exception innerException)
        : base(message, innerException) => Nombre = string.Empty;

    public string Nombre { get; }
}
