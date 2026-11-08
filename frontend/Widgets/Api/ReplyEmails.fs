namespace TwoPoint.Api.ReplyEmails

open TwoPoint.Api

/// The state of one stop token. Only an active token gives the post title.
[<RequireQualifiedAccess>]
type ReplyEmailsState =
  | Active of postTitle : string
  | Stopped
  | Unknown

[<RequireQualifiedAccess>]
module ReplyEmails =

  open Fable.Core
  open Fable.Core.JS
  open Fetch
  open Thoth.Json

  let private state : Decoder<ReplyEmailsState> =
    Decode.object (fun get -> get.Required.Field "state" Decode.string, get.Optional.Field "postTitle" Decode.string)
    |> Decode.andThen (fun found ->
      match found with
      | "active", Some postTitle -> Decode.succeed (ReplyEmailsState.Active postTitle)
      | "stopped", _ -> Decode.succeed ReplyEmailsState.Stopped
      | "unknown", _ -> Decode.succeed ReplyEmailsState.Unknown
      | state, _ -> Decode.fail $"The state '{state}' is unknown, or has no post title.")

  let private answer : Decoder<ApiResponse<ReplyEmailsState>> =
    Decode.object (fun get ->
      { Success = get.Required.Field "success" Decode.bool
        Message = get.Optional.Field "message" Decode.string
        Data = get.Optional.Field "data" state })

  let private invalid = { Success = false; Message = Some "An invalid response was received"; Data = None }

  let private request (url: string) (properties: RequestProperties list) : Promise<ApiResponse<ReplyEmailsState>> = promise {
    let! response = fetchUnsafe url (requestHeaders [ Accept "application/json" ] :: properties)
    let! text = response.text ()
    return Decode.fromString answer text |> Result.defaultValue invalid
  }

  let private url (api: string) (token: string) = $"{api}/api/blog/reply-emails/{encodeURIComponent token}"

  let read (api: string) (token: string) = request (url api token) []

  let stop (api: string) (token: string) = request $"{url api token}/stop" [ Method HttpMethod.POST ]
