var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.ElBruno_MagenticUI_App>("magentic-ui-app");

builder.Build().Run();
