var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.WebRTCAspire_Web>("webfrontend")
    //.WithEnvironment("Logging__LogLevel__Default", "Trace")
    .WithExternalHttpEndpoints();

builder.Build().Run();
