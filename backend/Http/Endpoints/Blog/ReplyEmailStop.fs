namespace TwoPoint.Http.Endpoints.Blog

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Posts.Queries
open TwoPoint.Core.Shared
open TwoPoint.Dependencies.Posts
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Data.Tables
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Threading

/// `State` is `active`, `stopped`, or `unknown`. An unknown token has no post title.
type ReplyEmailStopJson =
  { State : string
    PostTitle : string option }

module ReplyEmailStopMapping =

  let toJson (state : ReplyEmailStopState) =
    match state with
    | ReplyEmailStopState.Active title -> { State = "active"; PostTitle = Some (NonEmptyString.value title) }
    | ReplyEmailStopState.Stopped title -> { State = "stopped"; PostTitle = Some (NonEmptyString.value title) }
    | ReplyEmailStopState.Unknown -> { State = "unknown"; PostTitle = None }

  /// An unknown token is a state of the page, so it gets `200` too.
  let answer (result : Result<ReplyEmailStopState, string>) : ApiResponse<ReplyEmailStopJson> * HttpStatusCode =
    match result with
    | Ok state -> { Success = true; Message = None; Data = Some (toJson state) }, HttpStatusCode.OK
    | Error message -> { Success = false; Message = Some message; Data = None }, HttpStatusCode.InternalServerError

module internal ReplyEmailStopDependencies =

  let create (config : Config) (emailClient : EmailClient) (tableServiceClient : TableServiceClient) (logger : ILogger) =
    let postDependencies =
      PostDependencies(
        config.ValidRedirectUris |> List.map _.Uri,
        emailClient,
        config.Azure.EmailSender,
        tableServiceClient,
        logger
      )

    { new StopReplyEmails.Dependencies with
        member _.GetReplyEmailStop token = postDependencies.GetReplyEmailStop token
        member _.GetPostedCommentById commentId = postDependencies.GetPostedCommentById commentId
        member _.GetUtcNow () = DateTime.UtcNow
        member _.UpdateReplyEmailConsent post comment consent = postDependencies.UpdateReplyEmailConsent post comment consent }

  let write (req : HttpRequestData) (ct : CancellationToken) (result : Result<ReplyEmailStopState, string>) = task {
    let apiResponse, statusCode = ReplyEmailStopMapping.answer result
    let response = req.CreateResponse statusCode
    do! response.WriteAsJsonAsync(apiResponse, ct)
    return response
  }

type GetReplyEmailStop (
  config: Config,
  emailClient: EmailClient,
  logger: ILogger<GetReplyEmailStop>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Blog-ReplyEmails-Get")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "blog/reply-emails/{token}")>] req : HttpRequestData,
    token : string,
    ct : CancellationToken
  ) = task {
    logger.LogInformation "Processing 'Blog.ReplyEmails.Get' request"

    let getStop =
      GetReplyEmailStop.withDependencies (ReplyEmailStopDependencies.create config emailClient tableServiceClient logger)

    let! result = ct |> getStop token
    return! result |> Result.mapError string |> ReplyEmailStopDependencies.write req ct
  }

type StopReplyEmails (
  config: Config,
  emailClient: EmailClient,
  logger: ILogger<StopReplyEmails>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Blog-ReplyEmails-Stop")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "blog/reply-emails/{token}/stop")>] req : HttpRequestData,
    token : string,
    ct : CancellationToken
  ) = task {
    logger.LogInformation "Processing 'Blog.ReplyEmails.Stop' request"

    let stop =
      StopReplyEmails.withDependencies (ReplyEmailStopDependencies.create config emailClient tableServiceClient logger)

    let! result = ct |> stop token
    return! result |> Result.mapError _.Message |> ReplyEmailStopDependencies.write req ct
  }
