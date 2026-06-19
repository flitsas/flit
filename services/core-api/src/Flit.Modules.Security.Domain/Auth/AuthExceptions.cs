namespace Flit.Modules.Security.Domain.Auth;

public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException()
        : base("Invalid credentials.")
    {
    }
}

public sealed class AccountNotActiveException : Exception
{
    public AccountNotActiveException()
        : base("Account is not active.")
    {
    }
}

public sealed class AccountSuspendedException : Exception
{
    public AccountSuspendedException()
        : base("Account is temporarily suspended.")
    {
    }
}
