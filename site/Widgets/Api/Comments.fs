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

  let getCommentsForPost uri slug : Promise<ApiResponse<Comment list>> = promise {
    let! response =
      fetchUnsafe $"{uri}/api/blog/posts/{slug}/comments" [
        Method HttpMethod.GET
        requestHeaders [ Accept "application/json" ]
      ]

    let! text = response.text ()

    return
      Decode.Auto.fromString<ApiResponse<Comment list>>(text, caseStrategy = CamelCase)
      |> Result.defaultValue invalid
  }

  let validateCommenter uri (commenterValidation: CommenterValidation) : Promise<ApiResponse<unit>> = promise {
    let body = Encode.Auto.toString commenterValidation

    let! response =
      fetchUnsafe $"{uri}/api/blog/commenters" [
        Method HttpMethod.POST
        requestHeaders [ Accept "application/json" ]
        Body (U3.Case3 body)
      ]

    let! text = response.text ()

    return
      Decode.Auto.fromString<ApiResponse<unit>>(text, caseStrategy = CamelCase)
      |> Result.defaultValue invalid
  }

  let postComment uri slug (newComment: NewComment) : Promise<ApiResponse<unit>> = promise {
    let body = Encode.Auto.toString newComment

    let! response =
      fetchUnsafe $"{uri}/api/blog/posts/{slug}/comments" [
        Method HttpMethod.POST
        requestHeaders [ Accept "application/json" ]
        Body (U3.Case3 body)
      ]

    let! text = response.text ()

    return
      Decode.Auto.fromString<ApiResponse<unit>>(text, caseStrategy = CamelCase)
      |> Result.defaultValue invalid
  }
