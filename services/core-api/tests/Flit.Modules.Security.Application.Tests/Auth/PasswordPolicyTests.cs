using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("NewPass123")]
    [InlineData("Abcdef1g")]
    [InlineData("DemoPass1!")]
    public void IsCompliant_ValidPasswords_ReturnsTrue(string password)
    {
        PasswordPolicy.IsCompliant(password).Should().BeTrue();
    }

    [Theory]
    [InlineData("Ab1")]            // muy corta
    [InlineData("abcdef12")]       // sin mayúscula
    [InlineData("ABCDEF12")]       // sin minúscula
    [InlineData("Abcdefgh")]       // sin dígito
    [InlineData("")]               // vacía
    public void IsCompliant_InvalidPasswords_ReturnsFalse(string password)
    {
        PasswordPolicy.IsCompliant(password).Should().BeFalse();
    }
}
