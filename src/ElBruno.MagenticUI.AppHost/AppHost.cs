var builder = DistributedApplication.CreateBuilder(args);

#pragma warning disable ASPIREBROWSERLOGS001
builder.AddProject<Projects.ElBruno_MagenticUI_App>("magentic-ui-app")
    .WithHttpsEndpoint(port: 7127, name: "https")
    .WithHttpEndpoint(port: 5258, name: "http")
    .WithBrowserLogs();
#pragma warning restore ASPIREBROWSERLOGS001

builder.Build().Run();
