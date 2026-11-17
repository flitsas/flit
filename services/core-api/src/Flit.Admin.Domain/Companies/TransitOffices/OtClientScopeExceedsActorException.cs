namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Un usuario del OT con alcance <c>especificos</c> intentó entregar (a otro usuario o a un mandatario) compañías que
/// ESTÁN fuera de su propio alcance, o el modo <c>todos</c>. La API responde 403 <see cref="Code"/> y no guarda nada.
/// El mensaje no lleva datos de la compañía ajena.
/// </summary>
public sealed class OtClientScopeExceedsActorException : Exception
{
    public const string Code = "CLIENT_SCOPE_EXCEEDS_ACTOR";

    public const string DefaultMessage = "No puedes dar más acceso del que tú tienes: solo compañías de tu propio alcance.";

    public OtClientScopeExceedsActorException()
        : base(DefaultMessage)
    {
    }

    public OtClientScopeExceedsActorException(string message)
        : base(message)
    {
    }

    public OtClientScopeExceedsActorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
