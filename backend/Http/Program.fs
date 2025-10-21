open Azure.Communication.Email
open TwoPoint.Http

open Azure.Data.Tables
open Azure.Identity
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Builder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Symbolica.Extensions.Configuration.FSharp

open System
open System.Text.Json

[<EntryPoint>]
let main args =
  let builder = FunctionsApplication.CreateBuilder(args)
    
  builder.ConfigureFunctionsWebApplication() |> ignore
  
  builder.Configuration.AddJsonFile("local.settings.json") |> ignore
  
  builder.Services.AddHttpClient() |> ignore
  builder.Services.Configure<JsonSerializerOptions>(
    Action<JsonSerializerOptions>(fun options ->
      options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
      options.PropertyNameCaseInsensitive <- true
    )
  ) |> ignore
  
  builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights()
    |> ignore
    
  let config = builder.Configuration |> Config.bind |> BindResult.getOrFail
  
  // SETUP DEPENDENCIES
  
  // Azure credential
  let opts = DefaultAzureCredentialOptions()
  opts.TenantId <- config.Azure.EntraTenantId.ToString()
  
  if not (builder.Environment.IsDevelopment())
  then opts.ManagedIdentityClientId <- config.Azure.ManagedIdentityClientId.ToString()
  let credential = DefaultAzureCredential(opts)
  
  // Azure Table Storage
  let tableServiceClient = TableServiceClient(config.Azure.TableStorageUri, credential)
  
  // Email client
  let resourceEndpoint = config.Azure.CommsResourceEndpoint
  let emailClient = EmailClient(resourceEndpoint,  credential)

  builder.Services.AddScoped<Config>(fun _ -> config) |> ignore
  builder.Services.AddScoped<EmailClient>(fun _ -> emailClient) |> ignore
  builder.Services.AddScoped<TableServiceClient>(fun _ -> tableServiceClient) |> ignore
    
  builder.Build().Run()
  
  0
