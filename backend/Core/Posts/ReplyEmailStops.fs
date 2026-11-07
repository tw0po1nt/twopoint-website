namespace TwoPoint.Core.Posts

open TwoPoint.Core.Shared

open System

/// What the stop page shows for one stop token.
[<RequireQualifiedAccess>]
type ReplyEmailStopState =
  | Active of postTitle : NonEmptyString
  | Stopped of postTitle : NonEmptyString
  | Unknown

[<RequireQualifiedAccess>]
type ReplyEmailStopDecision =
  | WriteStop of post : Slug * comment : CommentId * consent : ReplyEmailConsent * postTitle : NonEmptyString
  | NoWrite of ReplyEmailStopState

[<RequireQualifiedAccess>]
module ReplyEmailStopRule =

  let private consentOf (token : ReplyEmailStopToken) (comment : PostedComment option) =
    comment
    |> Option.bind (fun posted ->
      posted.Comment.ReplyEmails
      |> Option.filter (fun consent -> consent.StopToken = token)
      |> Option.map (fun consent -> posted, consent))

  /// A deleted comment, or a comment with no consent for the token, gives `Unknown`.
  let state (token : ReplyEmailStopToken) (comment : PostedComment option) : ReplyEmailStopState =
    match consentOf token comment with
    | Some (posted, { StopDate = None }) -> ReplyEmailStopState.Active posted.PostTitle
    | Some (posted, { StopDate = Some _ }) -> ReplyEmailStopState.Stopped posted.PostTitle
    | None -> ReplyEmailStopState.Unknown

  let stop (utcNow : DateTime) (token : ReplyEmailStopToken) (comment : PostedComment option) : ReplyEmailStopDecision =
    match consentOf token comment with
    | Some (posted, ({ StopDate = None } as consent)) ->
      ReplyEmailStopDecision.WriteStop (posted.Post, posted.Comment.Id, { consent with StopDate = Some utcNow }, posted.PostTitle)
    | Some (_, { StopDate = Some _ })
    | None -> ReplyEmailStopDecision.NoWrite (state token comment)


namespace TwoPoint.Core.Posts.Queries

open TwoPoint.Core.Posts
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

[<RequireQualifiedAccess>]
module GetReplyEmailStop =

  /// The text of the token comes from the stop link, so a token that is not a GUID gives `Unknown`.
  type Query = string -> CancellableTask<QueryResult<ReplyEmailStopState>>

  type Dependencies =
    abstract member GetReplyEmailStop: token: ReplyEmailStopToken -> DependencyResult<ReplyEmailStop option>
    abstract member GetPostedCommentById: commentId: CommentId -> DependencyResult<PostedComment option>

  let findComment (dependencies : Dependencies) (token : ReplyEmailStopToken) : DependencyResult<PostedComment option> =
    cancellableTaskResult {
      match! dependencies.GetReplyEmailStop token with
      | Some (stop : ReplyEmailStop) -> return! dependencies.GetPostedCommentById stop.Comment
      | None -> return None
    }

  let withDependencies (dependencies : Dependencies) : Query =
    fun token -> cancellableTaskResult {
      match ReplyEmailStopToken.create token with
      | Error _ -> return ReplyEmailStopState.Unknown
      | Ok token ->
        let! comment = findComment dependencies token |> CancellableTaskResult.mapError QueryError.Dependency
        return ReplyEmailStopRule.state token comment
    }


namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Queries
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

[<RequireQualifiedAccess>]
module StopReplyEmails =

  /// The text of the token comes from the stop link, so a token that is not a GUID gives `Unknown`.
  type Action = string -> CancellableTask<Result<ReplyEmailStopState, DependencyError>>

  type Dependencies =
    inherit GetReplyEmailStop.Dependencies
    abstract member GetUtcNow: unit -> DateTime
    abstract member UpdateReplyEmailConsent:
      post: TwoPoint.Core.Posts.Slug -> comment: CommentId -> consent: ReplyEmailConsent -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =
    fun token -> cancellableTaskResult {
      match ReplyEmailStopToken.create token with
      | Error _ -> return ReplyEmailStopState.Unknown
      | Ok token ->
        let! comment = GetReplyEmailStop.findComment dependencies token

        match ReplyEmailStopRule.stop (dependencies.GetUtcNow()) token comment with
        | ReplyEmailStopDecision.WriteStop (post, comment, consent, postTitle) ->
          do! dependencies.UpdateReplyEmailConsent post comment consent
          return ReplyEmailStopState.Stopped postTitle
        | ReplyEmailStopDecision.NoWrite state -> return state
    }
