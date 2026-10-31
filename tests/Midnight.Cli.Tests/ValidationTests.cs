using Midnight.Cli.Core;

namespace Midnight.Cli.Tests;

public class ValidationTests
{
    [Theory]
    [InlineData("panel.example.com", true)]
    [InlineData("node-1.games.example.de", true)]
    [InlineData("localhost", false)]
    [InlineData("203.0.113.5", false)]
    [InlineData("-bad.example.com", false)]
    [InlineData("https://panel.example.com", false)]
    public void Domain(string value, bool expected) => Assert.Equal(expected, Validation.IsDomain(value));

    [Theory]
    [InlineData("panel.example.com", true, true)]
    [InlineData("203.0.113.5", true, false)]
    [InlineData("203.0.113.5", false, true)]
    [InlineData("panel.example.com", false, true)]
    [InlineData("1.2.3", false, false)]
    [InlineData("256.1.1.1", false, false)]
    public void SslRequiresDomain(string value, bool ssl, bool expected) => Assert.Equal(expected, Validation.IsAddress(value, ssl));

    [Theory]
    [InlineData("admin@example.com", true)]
    [InlineData("admin@example", false)]
    [InlineData("no-at-sign.com", false)]
    public void Email(string value, bool expected) => Assert.Equal(expected, Validation.IsEmail(value));

    [Theory]
    [InlineData("Max.Mustermann+test@example.com", "max.mustermann-test")]
    [InlineData("a@example.com", "admin")]
    [InlineData("-.x_y-@example.com", "x_y")]
    public void UsernameFromEmail(string email, string expected)
    {
        var username = Validation.UsernameFromEmail(email);

        Assert.Equal(expected, username);
        Assert.True(Validation.IsUsername(username));
    }

    [Fact]
    public void UsernameFromLongEmailIsCut()
        => Assert.Equal(32, Validation.UsernameFromEmail(new string('a', 50) + "@example.com").Length);

    [Fact]
    public void MaskHidesSecrets()
        => Assert.Equal("password=*** again ***", Shell.Mask("password=hunter22 again hunter22", ["hunter22", ""]));
}
