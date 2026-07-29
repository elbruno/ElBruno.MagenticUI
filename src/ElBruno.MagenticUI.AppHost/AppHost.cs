var builder = DistributedApplication.CreateBuilder(args);

#pragma warning disable ASPIREBROWSERLOGS001
builder.AddProject<Projects.ElBruno_MagenticUI_App>("magentic-ui-app")
    .WithBrowserLogs();
#pragma warning restore ASPIREBROWSERLOGS001

builder.Build().Run();
