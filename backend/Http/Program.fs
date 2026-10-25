open TwoPoint.Dependencies.Notifications
open TwoPoint.Dependencies.Posts
open TwoPoint.Dependencies.Util
open TwoPoint.Http
open TwoPoint.Http.Endpoints.Auth

open Azure.Communication.Email
open Azure.Data.Tables
open Azure.Identity
open Azure.Messaging.ServiceBus
open Azure.Storage.Blobs
open Azure.Storage.Blobs.Models
open Azure.Security.KeyVault.Secrets
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Builder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Identity.Web
open Symbolica.Extensions.Configuration.FSharp

open System
open System.Net.Http
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

  // Azure Blob Storage: the post store and the assets. Azurite takes no token, so the local stage signs with its development account.
  let blobClientFor (uri : Uri) =
    if TwoPoint.Site.Render.Azurite.isUrl uri
    then BlobServiceClient "UseDevelopmentStorage=true"
    else BlobServiceClient(uri, credential)

  let postStoreClient = blobClientFor config.Azure.PostStoreUri
  let assetsClient = blobClientFor config.Azure.AssetsUri

  // Azurite starts with no container. The deployed container exists and is public.
  if TwoPoint.Site.Render.Azurite.isUrl config.Azure.AssetsUri then
    assetsClient.GetBlobContainerClient("assets").CreateIfNotExists(PublicAccessType.Blob) |> ignore

  // Email client
  let resourceEndpoint = config.Azure.CommsResourceEndpoint
  let emailClient = EmailClient(resourceEndpoint,  credential)

  // Service Bus client
  let serviceBusClient = ServiceBusClient(config.Azure.ServiceBusUri.Host, credential)

  builder.Services.AddSingleton<Azure.Core.TokenCredential>(fun _ -> credential :> Azure.Core.TokenCredential) |> ignore
  builder.Services.AddScoped<Config>(fun _ -> config) |> ignore
  builder.Services.AddScoped<EmailClient>(fun _ -> emailClient) |> ignore
  builder.Services.AddScoped<TableServiceClient>(fun _ -> tableServiceClient) |> ignore
  builder.Services.AddSingleton<BlobServiceClient>(fun _ -> postStoreClient) |> ignore
  let previewImages =
    TwoPoint.Dependencies.Posts.ImageSizeCache(
      TwoPoint.Site.Render.ImageFetch.http (new System.Net.Http.HttpClient(Timeout = TimeSpan.FromSeconds 10.0)),
      TimeSpan.FromMinutes 5.0,
      fun () -> DateTimeOffset.UtcNow
    )
    :> TwoPoint.Site.Render.IImageSize

  builder.Services.AddSingleton<TwoPoint.Site.Render.IImageSize>(fun _ -> previewImages) |> ignore
  builder.Services.AddSingleton<TwoPoint.Dependencies.Posts.BlobTextCache>(fun _ -> TwoPoint.Dependencies.Posts.BlobTextCache()) |> ignore
  builder.Services.AddSingleton<TwoPoint.Dependencies.Posts.PublishedParseCache>(fun _ -> TwoPoint.Dependencies.Posts.PublishedParseCache config.Stage) |> ignore
  builder.Services.AddSingleton<AssetsClient>(fun _ -> { Service = assetsClient }) |> ignore
  builder.Services.AddScoped<ServiceBusClient>(fun _ -> serviceBusClient) |> ignore
  builder.Services.AddSingleton<WebPushSetup>(fun services ->
    match config.WebPush with
    | None -> WebPushNotConfigured
    | Some webPush ->
      let privateKey =
        match webPush.PrivateKey with
        | Inline privateKey -> privateKey
        | KeyVault(secretName, vaultUri) -> SecretClient(vaultUri, credential).GetSecret(secretName).Value.Value

      let keys =
        { VapidKeys.Subject = webPush.Subject
          PublicKey = Base64Url.decode webPush.PublicKey
          PrivateKey = Base64Url.decode privateKey }

      WebPushConfigured(
        WebPushSender(
          services.GetRequiredService<IHttpClientFactory>().CreateClient(),
          keys,
          services.GetRequiredService<ILoggerFactory>().CreateLogger<WebPushSender>()
        )
      ))
  |> ignore

  builder.Build().Run()

  0
