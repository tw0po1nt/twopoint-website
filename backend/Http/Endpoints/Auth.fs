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
      
      // Log authorization header
      let authHeader =
        match req.Headers.TryGetValues("Authorization") with
        | true, values -> String.Join(", ", values)
        | false, _ -> "missing"
      logger.LogInformation("Authorization header: {auth}", authHeader)
      
      logger.LogInformation("Checking auth for '{op}'. Running in Azure: {azure}, Identity count: {count}",
        op, isRunningInAzure, req.Identities |> Seq.length)
      
      req.Identities
      |> Seq.iter (fun identity ->
        logger.LogInformation("Identity: {name}, IsAuthenticated: {auth}, Type: {type}",
          identity.Name, identity.IsAuthenticated, identity.AuthenticationType))
      
      let isAuthenticated = not isRunningInAzure || req.Identities |> Seq.exists _.IsAuthenticated
      let allowedUserIds = ["ad1209ae-c4cc-4360-953a-ccbab9d68c83"]
      let userId = 
        req.Identities 
        |> Seq.tryPick (fun id -> 
            id.Claims 
            |> Seq.tryFind (fun c -> c.Type = "http://schemas.microsoft.com/identity/claims/objectidentifier")
            |> Option.map _.Value)
      let isAllowedUser =
        userId
        |> Option.map (fun id -> allowedUserIds |> List.contains id)
        |> Option.defaultValue false
      
      if not (isAuthenticated && isAllowedUser) then
        logger.LogWarning("Unauthenticated request to '{op}'", op)
        let response = req.CreateResponse HttpStatusCode.Unauthorized
        do! response.WriteAsJsonAsync({| error = "Authentication required" |}, ct)
        return response
      else
        return! fn
    }
