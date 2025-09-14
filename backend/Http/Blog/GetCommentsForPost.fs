namespace TwoPoint.Http.Blog

open System.Net
open System.Threading.Tasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

type GetCommentsForPost (logger : ILogger<GetCommentsForPost>) =
  
  [<Function("Blog-Posts-GetComments")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "blog/posts/{slug}/comments")>] req : HttpRequestData,
    slug : string
  ) =
    let response = req.CreateResponse HttpStatusCode.OK
    logger.LogInformation("Processing 'Blog.Posts.GetComments' request with slug '{slug}'", slug)
    Task.FromResult response
