namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts
open TwoPoint.Dependencies.Posts
open TwoPoint.Dependencies.Util
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Data.Tables
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Threading

type CommenterCommentsDto =
  { Commenter : AdminCommenterDto
    Comments : PostedCommentDto list
    /// The number of replies from other writers that a delete of the commenter removes.
    OtherReplies : int }

[<AutoOpen>]
module CommenterCommentsExt =

  type CommenterComments with
    member this.ToDto() =
      { CommenterCommentsDto.Commenter = this.Commenter.ToDto()
        Comments = this.Comments |> List.map _.ToDto()
        OtherReplies = this.OtherReplies }

module GetCommenterQuery = TwoPoint.Core.Posts.Queries.GetCommenter

type GetCommenter (
  config: Config,
  emailClient: EmailClient,
  logger : ILogger<GetCommenter>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Commenters-Get")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/commenters/{validationId}")>] req : HttpRequestData,
    context : FunctionContext,
    validationId : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Commenters.Get"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with validation id '{validationId}'", op, validationId)
        let response = req.CreateResponse HttpStatusCode.OK
        let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri

        // Dependencies
        let postDependencies = PostDependencies(
          validRedirectUris,
          emailClient,
          config.Azure.EmailSender,
          tableServiceClient,
          SystemDependencies.live,
          logger
        )
        let getCommenter = GetCommenterQuery.withDependencies {
          new GetCommenterQuery.Dependencies with

          member _.GetCommenterByValidationId validationId = postDependencies.GetCommenterByValidationId validationId
          member _.GetCommentsOfCommenter commenter = postDependencies.GetPostedCommentsOfCommenter commenter
          member _.GetComment post commentId = postDependencies.GetComment post commentId
          member _.GetRepliesOf topLevels = postDependencies.GetRepliesOf topLevels
          member _.Stage = config.Stage
        }

        let! commenterResult = getCommenter validationId

        let apiResponse, statusCode =
          match commenterResult with
          | Ok (Some commenter) ->
            { Success = true; Message = None; Data = Some (commenter.ToDto()) }, HttpStatusCode.OK
          | Ok None ->
            { Success = false; Message = Some $"Commenter '{validationId}' not found"; Data = None }, HttpStatusCode.NotFound
          | Error queryError when queryError.IsValidation ->
            { Success = false; Message = Some (queryError.ToString()); Data = None }, HttpStatusCode.BadRequest
          | Error queryError ->
            { Success = false; Message = Some (queryError.ToString()); Data = None }, HttpStatusCode.InternalServerError

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
