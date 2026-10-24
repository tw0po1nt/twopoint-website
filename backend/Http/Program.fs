open TwoPoint.Http
open TwoPoint.Http.Endpoints.Auth

open Azure.Communication.Email
open Azure.Data.Tables
open Azure.Identity
open Azure.Messaging.ServiceBus
open Azure.Storage.Blobs
open FirebaseAdmin.Messaging
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Builder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Identity.Web
open Symbolica.Extensions.Configuration.FSharp

open System
open System.Text.Json

[<EntryPoint>]
let main args =
  let builder = FunctionsApplication.CreateBuilder args

  builder.ConfigureFunctionsWebApplication() |> ignore

  if builder.Environment.IsDevelopment() then
    builder.Configuration.AddJsonFile "local.settings.json" |> ignore

  builder.Services.AddMicrosoftIdentityWebApiAuthentication builder.Configuration |> ignore
  builder.Services.AddAuthorization() |> ignore

  builder.UseMiddleware<AuthenticationMiddleware>() |> ignore

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
  let credential = DefaultAzureCredential opts

  // Azure Table Storage
  let tableServiceClient = TableServiceClient(config.Azure.TableStorageUri, credential)

  // Azure Blob Storage: the post store. Azurite takes no token, so the local stage signs with its development account.
  let postStoreClient =
    if TwoPoint.Site.Render.Azurite.isUrl config.Azure.PostStoreUri
    then BlobServiceClient "UseDevelopmentStorage=true"
    else BlobServiceClient(config.Azure.PostStoreUri, credential)

  // Email client
  let resourceEndpoint = config.Azure.CommsResourceEndpoint
  let emailClient = EmailClient(resourceEndpoint,  credential)

  // Service Bus client
  let serviceBusClient = ServiceBusClient(config.Azure.ServiceBusUri.Host, credential)

  // Firebase Admin SDK
  FirebaseInitializer.initialize config (Some credential)
  |> Async.AwaitTask
  |> Async.RunSynchronously
  |> ignore

  builder.Services.AddSingleton<Azure.Core.TokenCredential>(fun _ -> credential :> Azure.Core.TokenCredential) |> ignore
  builder.Services.AddScoped<Config>(fun _ -> config) |> ignore
  builder.Services.AddScoped<EmailClient>(fun _ -> emailClient) |> ignore
  builder.Services.AddScoped<TableServiceClient>(fun _ -> tableServiceClient) |> ignore
  builder.Services.AddSingleton<BlobServiceClient>(fun _ -> postStoreClient) |> ignore
  builder.Services.AddScoped<ServiceBusClient>(fun _ -> serviceBusClient) |> ignore
  builder.Services.AddSingleton<FirebaseMessaging>(fun _ -> FirebaseMessaging.DefaultInstance) |> ignore

  builder.Build().Run()

  0
