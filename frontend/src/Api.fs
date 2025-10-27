namespace TwoPoint.Api

type ApiResponse<'T> =
  { Success : bool
    Message : string option
    Data : 'T option }

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
  open Fable.SimpleHttp
  open Thoth.Json


  let getCommentsForPost uri slug : Promise<ApiResponse<Comment list>> = promise {
    let! response =
      Http.request $"{uri}/api/blog/posts/{slug}/comments"
      |> Http.method GET
      |> Http.header (Headers.accept "application/json")
      |> Http.send
      |> Async.StartAsPromise
    
    return
      Decode.Auto.fromString<ApiResponse<Comment list>>(response.responseText, caseStrategy = CamelCase)
      |> Result.defaultValue { Success = false; Message = Some "An invalid response was received"; Data = None }
  }

  let validateCommenter uri (commenterValidation: CommenterValidation) : Promise<ApiResponse<unit>> = promise {
    let body = Encode.Auto.toString commenterValidation

    let! response =
      Http.request $"{uri}/api/blog/commenters"
      |> Http.method POST
      |> Http.header (Headers.accept "application/json")
      |> Http.content (BodyContent.Text body)
      |> Http.send
      |> Async.StartAsPromise
    
    return
      Decode.Auto.fromString<ApiResponse<unit>>(response.responseText, caseStrategy = CamelCase)
      |> Result.defaultValue { Success = false; Message = Some "An invalid response was received"; Data = None }
  }

  let postComment uri slug (newComment: NewComment) : Promise<ApiResponse<unit>> = promise {
    let body = Encode.Auto.toString newComment

    let! response =
      Http.request $"{uri}/api/blog/posts/{slug}/comments"
      |> Http.method POST
      |> Http.header (Headers.accept "application/json")
      |> Http.content (BodyContent.Text body)
      |> Http.send
      |> Async.StartAsPromise
    
    return
      Decode.Auto.fromString<ApiResponse<unit>>(response.responseText, caseStrategy = CamelCase)
      |> Result.defaultValue { Success = false; Message = Some "An invalid response was received"; Data = None }
  }
