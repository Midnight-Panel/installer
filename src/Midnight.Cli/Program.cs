using Midnight.Cli.Commands;
using Spectre.Console.Cli;

[assembly: System.Runtime.Versioning.UnsupportedOSPlatform("windows")]

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("midnight");
    config.SetApplicationVersion(Midnight.Cli.Core.AppInfo.Version);

    config.AddCommand<InstallCommand>("install")
        .WithDescription("Install the panel and/or the demon (interactive wizard)")
        .WithExample("install")
        .WithExample("install", "demon", "--ssl", "--demon-address", "node1.example.com");

    config.AddCommand<StatusCommand>("status").WithDescription("Overview of everything installed on this server");
    config.AddCommand<ServiceCommand>("start").WithDescription("Start panel and/or demon").WithExample("start", "panel");
    config.AddCommand<ServiceCommand>("stop").WithDescription("Stop panel and/or demon");
    config.AddCommand<ServiceCommand>("restart").WithDescription("Restart panel and/or demon").WithExample("restart", "demon");
    config.AddCommand<LogsCommand>("logs").WithDescription("Show logs of panel, demon, db, proxy or installer").WithExample("logs", "panel", "-f");
    config.AddCommand<UpdateCommand>("update").WithDescription("Update panel and/or demon to the latest version");
    config.AddCommand<ConfigureCommand>("configure").WithDescription("Paste a new demon configuration from the panel").WithExample("configure", "demon");
    config.AddCommand<ConfigCommand>("config").WithDescription("Show (secrets hidden) or edit the configuration").WithExample("config", "demon", "--edit");
    config.AddCommand<CreateAdminCommand>("create-admin").WithDescription("Create another panel administrator");
    config.AddCommand<BackupCommand>("backup").WithDescription("Back up the panel database and storage");
    config.AddCommand<DoctorCommand>("doctor").WithDescription("Check the installation for common problems");
    config.AddCommand<SslCommand>("ssl").WithDescription("Renew the demon certificate now").WithExample("ssl", "renew");
    config.AddCommand<UninstallCommand>("uninstall").WithDescription("Remove the panel or the demon");
    config.AddCommand<SelfUpdateCommand>("self-update").WithDescription("Update the midnight command itself");
    config.AddCommand<VersionCommand>("version").WithDescription("Show the version");
});

return await app.RunAsync(args);
