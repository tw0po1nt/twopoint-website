module TwoPoint.Http.Endpoints.Auth

open TwoPoint.Http

open IcedTasks

open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net

let runIfAuthorized
  (config : Config)
  (logger : ILogger<'T>)
  (req : HttpRequestData)
  (op : string)
  (fn: CancellableTask<HttpResponseData>) =
    cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      let isRunningInAzure = config.Values.AzureFunctionsEnvironment <> "Development"
      
      let isAuthenticated = not isRunningInAzure || req.Identities |> Seq.exists _.IsAuthenticated
      if not isAuthenticated then
        logger.LogWarning("Unauthenticated request to '{op}'", op)
        let response = req.CreateResponse HttpStatusCode.Unauthorized
        do! response.WriteAsJsonAsync({| error = "Authentication required" |}, ct)
        return response
      else
        return! fn
    }
