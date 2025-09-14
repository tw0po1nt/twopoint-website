open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

[<EntryPoint>]
let main args =
  let builder = FunctionsApplication.CreateBuilder(args)
  
  builder.ConfigureFunctionsWebApplication() |> ignore
  builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights()
    |> ignore
    
  builder.Build().Run()
  
  0
