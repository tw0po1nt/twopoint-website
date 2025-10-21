module TwoPoint.Http.Endpoints.Auth

open TwoPoint.Http

open IcedTasks

open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net

let runIfAuthorized
  (config : Config)
  (logger : ILogger<'T>)
  (req : HttpRequestData)
  (op : string)
  (fn: CancellableTask<HttpResponseData>) =
    cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      let isRunningInAzure = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") <> "Development"
      
      logger.LogInformation("Checking auth for '{op}'. Running in Azure: {azure}, Identity count: {count}",
        op, isRunningInAzure, req.Identities |> Seq.length)
      
      req.Identities
      |> Seq.iter (fun identity ->
        logger.LogInformation("Identity: {name}, IsAuthenticated: {auth}, Type: {type}",
          identity.Name, identity.IsAuthenticated, identity.AuthenticationType))
      
      let isAuthenticated = not isRunningInAzure || req.Identities |> Seq.exists _.IsAuthenticated
      if not isAuthenticated then
        logger.LogWarning("Unauthenticated request to '{op}'", op)
        let response = req.CreateResponse HttpStatusCode.Unauthorized
        do! response.WriteAsJsonAsync({| error = "Authentication required" |}, ct)
        return response
      else
        return! fn
    }
