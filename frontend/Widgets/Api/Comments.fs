namespace TwoPoint.Api.Comments

open TwoPoint.Api

type Comment =
  { Id : string
    CreatedDate : string
    Commenter : string
    Content : string }

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

  let private postedComment : Decoder<PostedComment> =
    Decode.map2
      (fun comment approval -> { Comment = comment; Approval = approval })
      (Decode.Auto.generateDecoder<Comment>(caseStrategy = CamelCase))
      (Decode.field "approval" approval)

  let private postCommentAnswer : Decoder<PostCommentAnswer> =
    Decode.map3
      (fun success message comment -> { Success = success; Message = message; Comment = comment })
      (Decode.field "success" Decode.bool)
      (Decode.optional "message" Decode.string)
      (Decode.optional "comment" postedComment)

  let getCommentsForPost uri slug : Promise<ApiResponse<Comment list>> =
    request $"{uri}/api/blog/posts/{slug}/comments" [ Method HttpMethod.GET ]

  let validateCommenter uri (commenterValidation: CommenterValidation) : Promise<ApiResponse<unit>> =
    request $"{uri}/api/blog/commenters" [
      Method HttpMethod.POST
      Body(U3.Case3(Encode.Auto.toString commenterValidation))
    ]

  let postComment uri slug (newComment: NewComment) : Promise<PostCommentAnswer> =
    answer postCommentAnswer $"{uri}/api/blog/posts/{slug}/comments" [
      Method HttpMethod.POST
      Body(U3.Case3(Encode.Auto.toString newComment))
    ] { Success = false; Message = invalidMessage; Comment = None }
