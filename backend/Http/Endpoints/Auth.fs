module TwoPoint.Http.Endpoints.Auth

open IcedTasks

open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.JwtBearer
open Microsoft.AspNetCore.Http
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Azure.Functions.Worker.Middleware
open Microsoft.Extensions.Logging

open System.Net
open System.Security.Claims
open System.Threading.Tasks
open Microsoft.IdentityModel.Tokens

type AuthInfo =
  { Client : string
    UserExternalId : string }

let runIfAuthorized
  (logger : ILogger<'T>)
  (req : HttpRequestData)
  (claimsPrincipal : ClaimsPrincipal option)
  (op : string)
  (fn: AuthInfo -> CancellableTask<HttpResponseData>) =
    cancellableTask {
      let! ct = CancellableTask.getCancellationToken()      
      let identities =
        claimsPrincipal
        |> Option.map _.Identities
        |> Option.defaultValue Seq.empty
        |> Seq.toArray
      
      let claims = 
        claimsPrincipal
        |> Option.map _.Claims
        |> Option.defaultValue Seq.empty
        |> Seq.toArray
      
      let isAuthenticated = identities |> Array.exists _.IsAuthenticated
      
      let requiredUserIds = ["ad1209ae-c4cc-4360-953a-ccbab9d68c83"]
      let requiredScopes = ["access_as_user"]
      let userId = 
        claims 
        |> Array.tryFind (fun c -> c.Type = "http://schemas.microsoft.com/identity/claims/objectidentifier")
        |> Option.map _.Value
      
      let isRequiredUser = (
        userId
        |> Option.map (fun id -> requiredUserIds |> List.contains id)
        |> Option.defaultValue false
      )
      
      let scope = 
        claims 
        |> Array.tryFind (fun c -> c.Type = "http://schemas.microsoft.com/identity/claims/scope")
        |> Option.map _.Value
      
      let isRequiredScope = (
        scope
        |> Option.map (fun scp -> requiredScopes |> List.contains scp)
        |> Option.defaultValue false
      )
      
      let androidAppId = "e0505a28-fb17-42d3-8bbd-e98bf44f089e"
      let appId = 
        claims 
        |> Array.tryFind (fun c -> c.Type = "appid")
        |> Option.map _.Value
        
      let client =
        match appId with
        | Some id when id = androidAppId -> "android"
        | _ -> "unknown"
        
      let isKnownClient = client = "android"
          
      let client =
        match appId with
        | Some id when id = androidAppId -> "android"
        | _ -> "unknown"
      
      if not isAuthenticated then
        logger.LogWarning("Unauthenticated request to '{op}'", op)
        let response = req.CreateResponse HttpStatusCode.Unauthorized
        do! response.WriteAsJsonAsync({| error = "Authentication required" |}, ct)
        return response
      elif not (isRequiredUser && isRequiredScope) then
        logger.LogWarning("Authenticated request with an invalid token to '{op}'", op)
        let response = req.CreateResponse HttpStatusCode.Forbidden
        do! response.WriteAsJsonAsync({| error = "You are not allowed to perform this action" |}, ct)
        return response
      elif not isKnownClient then
        logger.LogWarning("Authenticated request with an unrecognized client to '{op}'", op)
        let response = req.CreateResponse HttpStatusCode.Forbidden
        do! response.WriteAsJsonAsync({| error = "Unrecognized client" |}, ct)
        return response
      else
        let authInfo =
          { UserExternalId = userId |> Option.get
            Client = client }

        let scopeDict = dict [ "UserExternalId", box authInfo.UserExternalId; "Client", box authInfo.Client ]
        use _ = logger.BeginScope(scopeDict)
        return! fn authInfo
    }

type AuthenticationMiddleware() =
  interface IFunctionsWorkerMiddleware with
    member _.Invoke(context: FunctionContext, next: FunctionExecutionDelegate) =
      let isHttpTrigger = 
        context.FunctionDefinition.InputBindings.Values
        |> Seq.exists (fun binding -> binding.Type = "httpTrigger")
        
      if not isHttpTrigger
      then next.Invoke(context)
      else
        let httpContext = context.GetHttpContext()
        let protectedSegments = PathString("/api/internal")
        let isProtectedEndpoint = httpContext.Request.Path.StartsWithSegments(protectedSegments)
        if not isProtectedEndpoint
        then next.Invoke(context)
        else
          task {
            try
              let! result = httpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme)
              if result.Succeeded then
                httpContext.User <- result.Principal
                do! next.Invoke(context)
              else
                raise result.Failure
            with
              | :? SecurityTokenInvalidAudienceException
              | :? SecurityTokenInvalidIssuerException ->
                httpContext.Response.StatusCode <- int HttpStatusCode.Forbidden
                do! httpContext.Response.CompleteAsync()
              | _ ->
                httpContext.Response.StatusCode <- int HttpStatusCode.Unauthorized
                do! httpContext.Response.CompleteAsync()
          } :> Task
