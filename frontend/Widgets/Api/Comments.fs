namespace TwoPoint.Api.Comments

open TwoPoint.Api

/// The writer of a comment. A comment from the author has no commenter name.
type Writer =
  | Commenter
  | Author

type Comment =
  { Id : string
    CreatedDate : string
    Commenter : string
    Content : string
    /// The ID of the top-level comment of a reply.
    ReplyTo : string option
    Writer : Writer }

type CommenterValidation =
  { EmailAddress : string
    Name : string option
    RedirectUri : string }

type NewComment =
  { ValidationId : string
    Comment : string }

type Approval =
  | Approved
  | AwaitsApproval

type PostedComment =
  { Comment : Comment
    Approval : Approval }

type PostCommentAnswer =
  { Success : bool
    Message : string option
    Comment : PostedComment option }

[<RequireQualifiedAccess>]
module Comments =

  open Fable.Core
  open Fable.Core.JS
  open Fetch
  open Thoth.Json

  let private invalidMessage = Some "An invalid response was received"

  let private invalid = { Success = false; Message = invalidMessage; Data = None }

  let private answer (decoder: Decoder<'Answer>) (url: string) (properties: RequestProperties list) (invalid: 'Answer) : Promise<'Answer> = promise {
    let! response = fetchUnsafe url (requestHeaders [ Accept "application/json" ] :: properties)
    let! text = response.text ()

    return
      Decode.fromString decoder text
      |> Result.defaultValue invalid
  }

  let inline private request<'T> (url: string) (properties: RequestProperties list) : Promise<ApiResponse<'T>> =
    answer (Decode.Auto.generateDecoder<ApiResponse<'T>>(caseStrategy = CamelCase)) url properties invalid

  let private approval : Decoder<Approval> =
    Decode.string
    |> Decode.map (fun name ->
      match name with
      | "approved" -> Approved
      | _ -> AwaitsApproval)

  let private writer : Decoder<Writer> =
    Decode.string
    |> Decode.map (fun name ->
      match name with
      | "author" -> Author
      | _ -> Commenter)

  /// An answer from a backend before replies has no `replyTo` and no `writer`.
  let private comment : Decoder<Comment> =
    Decode.object (fun get ->
      { Id = get.Required.Field "id" Decode.string
        CreatedDate = get.Required.Field "createdDate" Decode.string
        Commenter = get.Required.Field "commenter" Decode.string
        Content = get.Required.Field "content" Decode.string
        ReplyTo = get.Optional.Field "replyTo" Decode.string
        Writer = get.Optional.Field "writer" writer |> Option.defaultValue Commenter })

  let private comments : Decoder<ApiResponse<Comment list>> =
    Decode.object (fun get ->
      { Success = get.Required.Field "success" Decode.bool
        Message = get.Optional.Field "message" Decode.string
        Data = get.Optional.Field "data" (Decode.list comment) })

  let private postedComment : Decoder<PostedComment> =
    Decode.map2
      (fun comment approval -> { Comment = comment; Approval = approval })
      comment
      (Decode.field "approval" approval)

  let private postCommentAnswer : Decoder<PostCommentAnswer> =
    Decode.map3
      (fun success message comment -> { Success = success; Message = message; Comment = comment })
      (Decode.field "success" Decode.bool)
      (Decode.optional "message" Decode.string)
      (Decode.optional "comment" postedComment)

  let getCommentsForPost uri slug : Promise<ApiResponse<Comment list>> =
    answer comments $"{uri}/api/blog/posts/{slug}/comments" [ Method HttpMethod.GET ] invalid

  let validateCommenter uri (commenterValidation: CommenterValidation) : Promise<ApiResponse<unit>> =
    request $"{uri}/api/blog/commenters" [
      Method HttpMethod.POST
      Body(U3.Case3(Encode.Auto.toString commenterValidation))
    ]

  let previewComment uri (source: string) : Promise<ApiResponse<string>> =
    request $"{uri}/api/blog/comments/preview" [
      Method HttpMethod.POST
      Body(U3.Case3(Encode.toString 0 (Encode.object [ "content", Encode.string source ])))
    ]

  let postComment uri slug (newComment: NewComment) : Promise<PostCommentAnswer> =
    answer postCommentAnswer $"{uri}/api/blog/posts/{slug}/comments" [
      Method HttpMethod.POST
      Body(U3.Case3(Encode.Auto.toString newComment))
    ] { Success = false; Message = invalidMessage; Comment = None }
