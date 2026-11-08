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

  let private consentOf (found : (ReplyEmailStopToken * PostedComment) option) =
    found
    |> Option.bind (fun (token, posted) ->
      posted.Comment.ReplyEmails
      |> Option.filter (fun consent -> consent.StopToken = token)
      |> Option.map (fun consent -> posted, consent))

  /// A deleted comment, or a comment with no consent for the token, gives `Unknown`.
  let state (found : (ReplyEmailStopToken * PostedComment) option) : ReplyEmailStopState =
    match consentOf found with
    | Some (posted, { StopDate = None }) -> ReplyEmailStopState.Active posted.PostTitle
    | Some (posted, { StopDate = Some _ }) -> ReplyEmailStopState.Stopped posted.PostTitle
    | None -> ReplyEmailStopState.Unknown

  let stop (utcNow : DateTime) (found : (ReplyEmailStopToken * PostedComment) option) : ReplyEmailStopDecision =
    match consentOf found with
    | Some (posted, ({ StopDate = None } as consent)) ->
      ReplyEmailStopDecision.WriteStop (posted.Post, posted.Comment.Id, { consent with StopDate = Some utcNow }, posted.PostTitle)
    | Some (_, { StopDate = Some _ })
    | None -> ReplyEmailStopDecision.NoWrite (state found)


namespace TwoPoint.Core.Posts.Queries

open TwoPoint.Core.Posts
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

[<RequireQualifiedAccess>]
module GetReplyEmailStop =

  type Query = string -> CancellableTask<QueryResult<ReplyEmailStopState>>

  type Dependencies =
    abstract member GetReplyEmailStop: token: ReplyEmailStopToken -> DependencyResult<ReplyEmailStop option>
    abstract member GetPostedComment: post: Slug -> commentId: CommentId -> DependencyResult<PostedComment option>

  /// The text of the token comes from the stop link, so a token that is not a GUID finds nothing.
  let findComment
    (dependencies : Dependencies)
    (text : string)
    : DependencyResult<(ReplyEmailStopToken * PostedComment) option> =
    cancellableTaskResult {
      match ReplyEmailStopToken.create text with
      | Error _ -> return None
      | Ok token ->
        match! dependencies.GetReplyEmailStop token with
        | Some (stop : ReplyEmailStop) ->
          let! comment = dependencies.GetPostedComment stop.Post stop.Comment
          return comment |> Option.map (fun posted -> token, posted)
        | None -> return None
    }

  let withDependencies (dependencies : Dependencies) : Query =
    fun token -> cancellableTaskResult {
      let! found = findComment dependencies token |> CancellableTaskResult.mapError QueryError.Dependency
      return ReplyEmailStopRule.state found
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

  type Action = string -> CancellableTask<Result<ReplyEmailStopState, DependencyError>>

  type Dependencies =
    inherit GetReplyEmailStop.Dependencies
    abstract member GetUtcNow: unit -> DateTime
    abstract member UpdateReplyEmailConsent:
      post: TwoPoint.Core.Posts.Slug -> comment: CommentId -> consent: ReplyEmailConsent -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =
    fun token -> cancellableTaskResult {
      let! found = GetReplyEmailStop.findComment dependencies token

      match ReplyEmailStopRule.stop (dependencies.GetUtcNow()) found with
      | ReplyEmailStopDecision.WriteStop (post, comment, consent, postTitle) ->
        do! dependencies.UpdateReplyEmailConsent post comment consent
        return ReplyEmailStopState.Stopped postTitle
      | ReplyEmailStopDecision.NoWrite state -> return state
    }
