using Application;
using Infrastructure;
using QuestPDF.Infrastructure;
using Web.Bootstrap;
using Web.Extensions;

EnvironmentLoader.LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddAppLogging();

builder.Services.AddWebServices(builder.Configuration);

builder.Services.AddDatabase(builder.Configuration);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

QuestPDF.Settings.License = LicenseType.Community;
        

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.ApplyMigrationsAsync();
}

app.UseWebPipeline();
app.MapWebEndpoints();

app.Lifetime.ApplicationStarted.Register(() =>
{
    app.Logger.LogInformation("Application has started.");
});

app.Run();

public partial class Program { }
