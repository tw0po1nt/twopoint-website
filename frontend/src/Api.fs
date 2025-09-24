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
