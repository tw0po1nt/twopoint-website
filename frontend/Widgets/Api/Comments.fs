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

type PostedComment =
  { Id : string
    CreatedDate : string
    Commenter : string
    Content : string
    Approval : string }

/// The posted comment comes beside `data`, which stays null.
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

  let inline private answer<'Answer> (url: string) (properties: RequestProperties list) (invalid: 'Answer) : Promise<'Answer> = promise {
    let! response = fetchUnsafe url (requestHeaders [ Accept "application/json" ] :: properties)
    let! text = response.text ()

    return
      Decode.Auto.fromString<'Answer>(text, caseStrategy = CamelCase)
      |> Result.defaultValue invalid
  }

  let inline private request<'T> (url: string) (properties: RequestProperties list) : Promise<ApiResponse<'T>> =
    answer url properties invalid

  let getCommentsForPost uri slug : Promise<ApiResponse<Comment list>> =
    request $"{uri}/api/blog/posts/{slug}/comments" [ Method HttpMethod.GET ]

  let validateCommenter uri (commenterValidation: CommenterValidation) : Promise<ApiResponse<unit>> =
    request $"{uri}/api/blog/commenters" [
      Method HttpMethod.POST
      Body(U3.Case3(Encode.Auto.toString commenterValidation))
    ]

  let postComment uri slug (newComment: NewComment) : Promise<PostCommentAnswer> =
    answer $"{uri}/api/blog/posts/{slug}/comments" [
      Method HttpMethod.POST
      Body(U3.Case3(Encode.Auto.toString newComment))
    ] { Success = false; Message = invalidMessage; Comment = None }
