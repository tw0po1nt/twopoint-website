module TwoPoint.Http.Endpoints.Auth

open TwoPoint.Core.Shared

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

let private claimValue (claims : Claim array) (claimType : string) =
  claims |> Array.tryFind (fun c -> c.Type = claimType) |> Option.map _.Value

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

      let accessClaims =
        { ObjectId = claimValue claims "http://schemas.microsoft.com/identity/claims/objectidentifier"
          Scope = claimValue claims "http://schemas.microsoft.com/identity/claims/scope"
          AuthorizedParty = claimValue claims "azp" }

      let forbidden (message : string) = cancellableTask {
        let response = req.CreateResponse HttpStatusCode.Forbidden
        do! response.WriteAsJsonAsync({| error = message |}, ct)
        return response
      }

      if not isAuthenticated then
        logger.LogWarning("Unauthenticated request to '{op}'", op)
        let response = req.CreateResponse HttpStatusCode.Unauthorized
        do! response.WriteAsJsonAsync({| error = "Authentication required" |}, ct)
        return response
      else
        match Access.decide accessClaims with
        | Forbidden UnknownClient ->
          logger.LogWarning("Authenticated request with an unrecognized client to '{op}'", op)
          return! forbidden "Unrecognized client"
        | Forbidden (UnknownUser | UnknownScope) ->
          logger.LogWarning("Authenticated request with an invalid token to '{op}'", op)
          return! forbidden "You are not allowed to perform this action"
        | Allowed client ->
          let authInfo =
            { UserExternalId = accessClaims.ObjectId |> Option.get
              Client = client }

          let scopeDict = dict [ "UserExternalId", box authInfo.UserExternalId; "Client", box authInfo.Client ]
          use _ = logger.BeginScope scopeDict
          return! fn authInfo
    }

type AuthenticationMiddleware() =
  interface IFunctionsWorkerMiddleware with
    member _.Invoke(context: FunctionContext, next: FunctionExecutionDelegate) =
      let isHttpTrigger =
        context.FunctionDefinition.InputBindings.Values
        |> Seq.exists (fun binding -> binding.Type = "httpTrigger")

      if not isHttpTrigger
      then next.Invoke context
      else
        let httpContext = context.GetHttpContext()
        let protectedSegments = PathString "/api/internal"
        let isProtectedEndpoint = httpContext.Request.Path.StartsWithSegments protectedSegments
        if not isProtectedEndpoint
        then next.Invoke context
        else
          task {
            try
              let! result = httpContext.AuthenticateAsync JwtBearerDefaults.AuthenticationScheme
              if result.Succeeded then
                httpContext.User <- result.Principal
                do! next.Invoke context
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
