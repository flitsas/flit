using Xunit;

namespace Flit.Notificaciones.Tests;

/// <summary>Las pruebas que levantan el servicio contra el broker real comparten colas: corren una tras otra.</summary>
[CollectionDefinition(Nombre)]
public sealed class ColeccionBus
{
    public const string Nombre = "Broker real";
}
