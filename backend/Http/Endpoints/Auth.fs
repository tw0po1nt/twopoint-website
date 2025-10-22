module TwoPoint.Http.Endpoints.Auth

open TwoPoint.Http

open IcedTasks

open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Security.Claims

let runIfAuthorized
  (config : Config)
  (logger : ILogger<'T>)
  (req : HttpRequestData)
  (claimsPrincipal : ClaimsPrincipal option)
  (op : string)
  (fn: CancellableTask<HttpResponseData>) =
    cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      let isRunningInAzure = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") <> "Development"
      
      let identities =
        claimsPrincipal
        |> Option.map _.Identities
        |> Option.defaultValue Seq.empty
        |> Seq.toArray
      logger.LogInformation("Checking auth for '{op}'. Running in Azure: {azure}, Identity count: {count}",
        op, isRunningInAzure, identities.Length)
      
      let claims = 
        claimsPrincipal
        |> Option.map _.Claims
        |> Option.defaultValue Seq.empty
        |> Seq.toArray
        
      claims |> Array.iter (fun claim -> logger.LogInformation("ClaimType: {type}, Value: {auth}", claim.Type, claim.Value))
      
      let isAuthenticated = not isRunningInAzure || identities |> Array.exists _.IsAuthenticated
      
      let allowedUserIds = ["ad1209ae-c4cc-4360-953a-ccbab9d68c83"]
      let userId = 
        identities 
        |> Array.tryPick (fun id -> 
            id.Claims 
            |> Seq.tryFind (fun c -> c.Type = "http://schemas.microsoft.com/identity/claims/objectidentifier")
            |> Option.map _.Value)
      
      let isAllowedUser = (
        not isRunningInAzure ||
        userId
        |> Option.map (fun id -> allowedUserIds |> List.contains id)
        |> Option.defaultValue false
      )
      
      if not (isAuthenticated && isAllowedUser) then
        logger.LogWarning("Unauthenticated request to '{op}'", op)
        let response = req.CreateResponse HttpStatusCode.Unauthorized
        do! response.WriteAsJsonAsync({| error = "Authentication required" |}, ct)
        return response
      else
        return! fn
    }
