[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.Contract

// Each type and function here must compile on .NET and with Fable, so this file uses no Fable types.

type SubscribeProps =
  { /// The origin of the API, with no `/` at the end, for example `http://localhost:7071`.
    Api: string }

type CommentsProps =
  { /// The origin of the API, with no `/` at the end, for example `http://localhost:7071`.
    Api: string
    Slug: string }

type UnsubscribeProps =
  { /// The origin of the API, with no `/` at the end, for example `http://localhost:7071`.
    Api: string }

[<RequireQualifiedAccess>]
type Widget =
  | Subscribe of SubscribeProps
  | Comments of CommentsProps
  | Unsubscribe of UnsubscribeProps

/// The name of each case of `Widget`.
let names = [ "subscribe"; "comments"; "unsubscribe" ]

let name (widget: Widget) =
  match widget with
  | Widget.Subscribe _ -> "subscribe"
  | Widget.Comments _ -> "comments"
  | Widget.Unsubscribe _ -> "unsubscribe"

let toAttributes (widget: Widget) : (string * string) list =
  [ "data-widget", name widget
    match widget with
    | Widget.Subscribe props -> "data-api", props.Api
    | Widget.Comments props ->
      "data-api", props.Api
      "data-slug", props.Slug
    | Widget.Unsubscribe props -> "data-api", props.Api ]

let private find (attributes: (string * string) list) (key: string) =
  attributes |> List.tryFind (fun (name, _) -> name = key) |> Option.map snd

let private required (attributes: (string * string) list) (widget: string) (key: string) =
  match find attributes key with
  | Some value -> Ok value
  | None -> Error $"The {widget} mount has no '{key}' attribute."

let private api (widget: string) (value: string) =
  if (value.StartsWith "https://" || value.StartsWith "http://") && not (value.EndsWith "/") then
    Ok value
  else
    Error $"The 'data-api' of the {widget} mount is '{value}'. Give an http or https origin with no '/' at the end."

let private isSlugChar (c: char) =
  (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c = '-'

let private slug (widget: string) (value: string) =
  if value <> "" && Seq.forall isSlugChar value then
    Ok value
  else
    Error $"The 'data-slug' of the {widget} mount is '{value}'. Give a post slug of lowercase letters, digits, and '-'."

let parse (attributes: (string * string) list) : Result<Widget, string> =
  match find attributes "data-widget" with
  | Some "subscribe" ->
    required attributes "subscribe" "data-api"
    |> Result.bind (api "subscribe")
    |> Result.map (fun api -> Widget.Subscribe { Api = api })
  | Some "comments" ->
    required attributes "comments" "data-api"
    |> Result.bind (api "comments")
    |> Result.bind (fun api ->
      required attributes "comments" "data-slug"
      |> Result.bind (slug "comments")
      |> Result.map (fun slug -> Widget.Comments { Api = api; Slug = slug }))
  | Some "unsubscribe" ->
    required attributes "unsubscribe" "data-api"
    |> Result.bind (api "unsubscribe")
    |> Result.map (fun api -> Widget.Unsubscribe { Api = api })
  | Some other ->
    let known = String.concat ", " names
    Error $"The widget '{other}' is unknown. Use one of these names: {known}."
  | None -> Error "The mount has no 'data-widget' attribute."
