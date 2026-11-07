namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Http
open TwoPoint.Http.Endpoints
open TwoPoint.Core.Posts
open TwoPoint.Dependencies.Posts

open Azure.Communication.Email
open Azure.Data.Tables
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Threading

type AdminCommenterDto =
  { Id : string
    Email : string
    Name : string
    Status : string
    CreatedDate : DateTime }

/// `Excerpt` is the start of the plain text, and `Content` is the full HTML.
type AdminTopLevelDto =
  { Id : string
    Commenter : AdminCommenterDto option
    Writer : string
    Status : string
    Excerpt : string
    Content : string }

type AdminCommentDto =
  { Id : string
    CreatedDate : DateTime
    Commenter : AdminCommenterDto option
    Status : string
    Content : string
    /// The ID of the top-level comment of a reply.
    ReplyTo : string option
    /// `commenter` or `author`. A comment from the author has no commenter.
    Writer : string
    TopLevel : AdminTopLevelDto option }

[<AutoOpen>]
module CommentTypesExt =

  type Commenter with
    member this.ToDto() =
      { AdminCommenterDto.Id = this.ValidationId.ToString()
        Email = this.EmailAddress.ToString()
        Name = this.Name |> Option.map _.ToString() |> Option.defaultValue "Anonymous"
        Status = this.Status.ToString()
        CreatedDate = this.CreatedDate }

  let private writerName (writer : CommentWriter) =
    match writer with
    | CommentWriter.Commenter _ -> "commenter"
    | CommentWriter.Author -> "author"

  type RenderedComment with
    member this.ToAdminDto() =
      { AdminCommentDto.Id = this.Comment.Id.ToString()
        CreatedDate = this.Comment.CreatedDate
        Commenter = this.Comment |> Comment.commenter |> Option.map _.ToDto()
        Status = this.Comment.Status.Approval.ToString()
        Content = this.Html
        ReplyTo = this.Comment.Parent |> Option.map string
        Writer = writerName this.Comment.Writer
        TopLevel = None }

  type TopLevelComment with
    member this.ToDto() =
      let comment = this.Comment.Comment

      { AdminTopLevelDto.Id = comment.Id.ToString()
        Commenter = comment |> Comment.commenter |> Option.map _.ToDto()
        Writer = writerName comment.Writer
        Status = comment.Status.Approval.ToString()
        Excerpt = this.Excerpt
        Content = this.Comment.Html }

  type AdminComment with
    member this.ToDto() =
      { this.Comment.ToAdminDto() with TopLevel = this.TopLevel |> Option.map _.ToDto() }

type AdminGetCommentsForPost (
  config: Config,
  emailClient: EmailClient,
  logger : ILogger<AdminGetCommentsForPost>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Posts-GetComments")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/posts/{slug}/comments")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.GetComments"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User
    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let response = req.CreateResponse HttpStatusCode.OK
        logger.LogInformation("Processing 'Admin.Posts.GetComments' request with slug '{slug}'", slug)

        let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri

        // Dependencies
        let postDependencies = PostDependencies(
          validRedirectUris,
          emailClient,
          config.Azure.EmailSender,
          tableServiceClient,
          logger
        )
        let getCommentsForPost = TwoPoint.Core.Posts.Queries.GetCommentsForPost.withDependencies {
          new TwoPoint.Core.Posts.Queries.GetCommentsForPost.Dependencies with
          
          member _.GetPostBySlug slug = postDependencies.GetPostBySlug slug
          member _.GetCommentsForPost approvals slug = postDependencies.GetCommentsForPost approvals slug
          member _.Stage = config.Stage
        }

        let! commentsResult = (slug, ct) ||> getCommentsForPost []

        let apiResponse, statusCode =
          match commentsResult with
          | Ok (Some comments) ->
            { Success = true; Message = None; Data = comments |> List.map _.ToDto() |> Some }, HttpStatusCode.OK
          | Ok None ->
            { Success = false; Message = Some $"Post ''{slug}'' not found"; Data = None }, HttpStatusCode.NotFound
          | Error queryError when queryError.IsValidation ->
            { Success = false;  Message = Some (queryError.ToString()); Data = None }, HttpStatusCode.BadRequest
          | Error queryError ->
            { Success = false; Message = Some (queryError.ToString()); Data = None }, HttpStatusCode.InternalServerError

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
