namespace TwoPoint.Http.Endpoints.Admin

open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http

open System.Net

type HealthCheck() =
  
  [<Function("HealthCheck")>]
  member _.Run ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")>] req : HttpRequestData) = task {
    let response = req.CreateResponse HttpStatusCode.OK
    do! response.WriteStringAsync("Healthy")
    return response
  }
