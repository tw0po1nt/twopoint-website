namespace TwoPoint.Http.Endpoints.Blog

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Api
open TwoPoint.Core.Posts.Dependencies
open TwoPoint.Http.Endpoints

open Azure.Data.Tables
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Threading

type CommentDto =
  { Id : string
    CreatedDate : DateTime
    Commenter : string
    Content : string }
  
[<AutoOpen>]
module PostTypesExt =
  
  type Comment with
    member this.ToDto() =
      { CommentDto.Id = this.Id.ToString()
        CreatedDate = this.CreatedDate
        Commenter = this.Commenter.Name |> Option.map _.ToString() |> Option.defaultValue "Anonymous"
        Content = this.Content.ToString() }


type GetCommentsForPost (logger : ILogger<GetCommentsForPost>, tableServiceClient: TableServiceClient) =
  
  [<Function("Blog-Posts-GetComments")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "blog/posts/{slug}/comments")>] req : HttpRequestData,
    slug : string,
    ct : CancellationToken
  ) = task {
    let response = req.CreateResponse HttpStatusCode.OK
    logger.LogInformation("Processing 'Blog.Posts.GetComments' request with slug '{slug}'", slug)
    
    // Dependencies
    let postDependencies = PostDependencies.live tableServiceClient logger
    let postQueries = PostQueries.withDependencies postDependencies
    
    let! commentsResult = ct |> postQueries.GetCommentsForPost slug
    
    let apiResponse, statusCode =
      match commentsResult with
      | Ok (Some comments) ->
        { Message = None; Data = comments |> List.map _.ToDto() |> Some }, HttpStatusCode.OK
      | Ok None ->
        { Message = Some $"Post '{slug}' not found"; Data = None }, HttpStatusCode.NotFound
      | Error queryError when queryError.IsValidation ->
        { Message = Some (queryError.ToString()); Data = None }, HttpStatusCode.BadRequest
      | Error queryError ->
        { Message = Some (queryError.ToString()); Data = None }, HttpStatusCode.InternalServerError
      
    response.StatusCode <- statusCode
    do! response.WriteAsJsonAsync(apiResponse, ct)
    return response
  }
