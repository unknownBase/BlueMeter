using BlueMeter.Services;
using Xunit;

namespace BlueMeter.Tests;

public sealed class StartupCommandTests
{
    [Fact]
    public void QuoteCommand_WrapsPathWithSpaces()
    {
        var command = StartupRegistrationService.QuoteCommand(@"C:\Program Files\BlueMeter\BlueMeter.exe");

        Assert.Equal("\"C:\\Program Files\\BlueMeter\\BlueMeter.exe\"", command);
    }

    [Fact]
    public void QuoteCommand_DoesNotDoubleQuoteButKeepsExistingQuotes()
    {
        Assert.Equal("\"C:\\BlueMeter.exe\"", StartupRegistrationService.QuoteCommand("\"C:\\BlueMeter.exe\""));
        Assert.Equal("\"C:\\BlueMeter.exe\"", StartupRegistrationService.QuoteCommand("  C:\\BlueMeter.exe  "));
    }

    [Fact]
    public void QuoteCommand_RejectsEmptyPath()
    {
        Assert.Throws<ArgumentException>(() => StartupRegistrationService.QuoteCommand("   "));
    }

    [Fact]
    public void BuildCommand_PointsAtRunningExecutable()
    {
        var command = StartupRegistrationService.BuildCommand();

        Assert.NotNull(command);
        Assert.StartsWith("\"", command);
        Assert.EndsWith("\"", command);
        Assert.Contains(".exe", command, StringComparison.OrdinalIgnoreCase);
    }
}
