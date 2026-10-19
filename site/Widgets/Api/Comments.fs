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

[<RequireQualifiedAccess>]
module Comments =

  open Fable.Core
  open Fable.Core.JS
  open Fetch
  open Thoth.Json

  let private invalid = { Success = false; Message = Some "An invalid response was received"; Data = None }

  let inline private request<'T> (url: string) (properties: RequestProperties list) : Promise<ApiResponse<'T>> = promise {
    let! response = fetchUnsafe url (requestHeaders [ Accept "application/json" ] :: properties)
    let! text = response.text ()

    return
      Decode.Auto.fromString<ApiResponse<'T>>(text, caseStrategy = CamelCase)
      |> Result.defaultValue invalid
  }

  let getCommentsForPost uri slug : Promise<ApiResponse<Comment list>> =
    request $"{uri}/api/blog/posts/{slug}/comments" [ Method HttpMethod.GET ]

  let validateCommenter uri (commenterValidation: CommenterValidation) : Promise<ApiResponse<unit>> =
    request $"{uri}/api/blog/commenters" [
      Method HttpMethod.POST
      Body(U3.Case3(Encode.Auto.toString commenterValidation))
    ]

  let postComment uri slug (newComment: NewComment) : Promise<ApiResponse<unit>> =
    request $"{uri}/api/blog/posts/{slug}/comments" [
      Method HttpMethod.POST
      Body(U3.Case3(Encode.Auto.toString newComment))
    ]
