using FluentAssertions;
using MsGraphCli.Core.Auth;
using Xunit;

namespace MsGraphCli.Tests.Unit;

public class ScopeRegistryTests
{
    [Fact]
    public void GetScopes_AlwaysIncludesBaseScopes()
    {
        string[] scopes = ScopeRegistry.GetScopes(["mail"], readOnly: false);

        scopes.Should().Contain("User.Read");
        scopes.Should().Contain("offline_access");
    }

    [Fact]
    public void GetScopes_Mail_ReadOnly_IncludesReadExcludesWrite()
    {
        string[] scopes = ScopeRegistry.GetScopes(["mail"], readOnly: true);

        scopes.Should().Contain("Mail.Read");
        scopes.Should().NotContain("Mail.Send");
    }

    [Fact]
    public void GetScopes_Mail_ReadWrite_IncludesBoth()
    {
        string[] scopes = ScopeRegistry.GetScopes(["mail"], readOnly: false);

        scopes.Should().Contain("Mail.Read");
        scopes.Should().Contain("Mail.Send");
        scopes.Should().Contain("Mail.ReadWrite");
    }

    [Fact]
    public void GetScopes_MultipleServices_CombinesScopes()
    {
        string[] scopes = ScopeRegistry.GetScopes(["mail", "calendar"], readOnly: false);

        scopes.Should().Contain("Mail.Read");
        scopes.Should().Contain("Mail.Send");
        scopes.Should().Contain("Calendars.ReadWrite");
    }

    [Fact]
    public void GetScopes_DeduplicatesSharedScopes()
    {
        // drive and excel both use Files.Read / Files.ReadWrite
        string[] scopes = ScopeRegistry.GetScopes(["drive", "excel"], readOnly: true);

        scopes.Where(s => s == "Files.Read").Should().HaveCount(1);
    }

    [Fact]
    public void GetScopes_UnknownService_Throws()
    {
        Action act = () => ScopeRegistry.GetScopes(["nonexistent"], readOnly: false);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown service*nonexistent*");
    }

    [Fact]
    public void GetAllScopes_ReadOnly_ExcludesAllWriteScopes()
    {
        string[] scopes = ScopeRegistry.GetAllScopes(readOnly: true);

        scopes.Should().NotContain("Mail.Send");
        scopes.Should().NotContain("Calendars.ReadWrite");
        scopes.Should().NotContain("Files.ReadWrite");
        scopes.Should().NotContain("Tasks.ReadWrite");
    }

    [Fact]
    public void ValidateServiceNames_ValidNames_DoesNotThrow()
    {
        Action act = () => ScopeRegistry.ValidateServiceNames(["mail", "calendar", "drive"]);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateServiceNames_InvalidName_Throws()
    {
        Action act = () => ScopeRegistry.ValidateServiceNames(["mail", "teams"]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown service*teams*");
    }

    [Fact]
    public void GetScopes_CaseInsensitive()
    {
        string[] scopes = ScopeRegistry.GetScopes(["MAIL", "Calendar"], readOnly: false);

        scopes.Should().Contain("Mail.Read");
        scopes.Should().Contain("Calendars.ReadWrite");
    }
}
