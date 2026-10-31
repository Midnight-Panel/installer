using System.Text.Json.Nodes;
using Midnight.Cli.Components;
using Midnight.Cli.Core;

namespace Midnight.Cli.Tests;

public class PanelStackTests
{
    private static PanelSettings Settings(bool ssl) => new()
    {
        Ssl = ssl,
        Address = ssl ? "panel.example.com" : "203.0.113.5",
        HttpPort = 8000,
        AdminEmail = "admin@example.com"
    };

    [Fact]
    public void CaddyWithSslUsesTheDomain()
    {
        var caddyfile = PanelComponent.BuildCaddyfile(Settings(true));

        Assert.Contains("email admin@example.com", caddyfile);
        Assert.Contains("panel.example.com {", caddyfile);
        Assert.Contains("reverse_proxy panel:8080", caddyfile);
    }

    [Fact]
    public void CaddyWithoutSslListensOnThePort()
    {
        var caddyfile = PanelComponent.BuildCaddyfile(Settings(false));

        Assert.StartsWith(":8000 {", caddyfile);
        Assert.DoesNotContain("email", caddyfile);
    }

    [Theory]
    [InlineData(true, "https://panel.example.com", "\"443:443\"")]
    [InlineData(false, "http://203.0.113.5:8000", "\"8000:8000\"")]
    public void ComposeContainsUrlAndPorts(bool ssl, string url, string port)
    {
        var compose = PanelComponent.BuildCompose(Settings(ssl));

        Assert.Contains($"MIDNIGHT_PublicUrl: {url}", compose);
        Assert.Contains(port, compose);
        Assert.Contains("${POSTGRES_PASSWORD}", compose);
        Assert.Contains($"subnet: {Paths.PanelSubnet}", compose);
        // compose files are indented with spaces only
        Assert.DoesNotContain('\t', compose);
    }
}

public class DemonConfigurationTests
{
    private const string PanelJson = """{"panelUrl":"https://panel.example.com","tokenId":"abc","token":"secret","httpPort":1234}""";

    [Fact]
    public void ExtractsJsonFromPastedText()
    {
        var json = DemonComponent.ExtractJson($"Copy this:\n{PanelJson}\n$ ");

        Assert.NotNull(json);
        Assert.Equal("abc", JsonNode.Parse(json)!["tokenId"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("no json here")]
    [InlineData("{ broken")]
    [InlineData("[1, 2]")]
    public void RejectsInvalidInput(string input) => Assert.Null(DemonComponent.ExtractJson(input));

    [Fact]
    public void MergeAppliesLocalSettings()
    {
        var merged = JsonNode.Parse(DemonComponent.MergeConfiguration(PanelJson, new DemonState { Ssl = true, Address = "node1.example.com", Port = 8443 }))!;

        Assert.Equal(8443, merged["httpPort"]!.GetValue<int>());
        Assert.Equal(Paths.DemonData, merged["dataPath"]!.GetValue<string>());
        Assert.True(merged["ssl"]!["enabled"]!.GetValue<bool>());
        Assert.Equal("/etc/letsencrypt/live/node1.example.com/fullchain.pem", merged["ssl"]!["certificatePath"]!.GetValue<string>());
        Assert.Equal("secret", merged["token"]!.GetValue<string>());
    }

    [Fact]
    public void MergeRequiresToken()
        => Assert.Throws<InstallerException>(() => DemonComponent.MergeConfiguration("""{"panelUrl":"https://panel.example.com","tokenId":"abc"}""", new DemonState()));

    [Fact]
    public void UnitRunsTheDemonBinary()
    {
        var unit = DemonComponent.BuildUnit();

        Assert.Contains($"ExecStart={Paths.DemonBinary}", unit);
        Assert.Contains("[Install]", unit);
    }
}
