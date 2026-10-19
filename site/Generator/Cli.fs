[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Cli

open System

type Command =
  | Build of Stage
  /// The container URI, for example `https://account.blob.core.windows.net/newsletter`.
  | Upload of container: Uri

let private stageNames = Stage.all |> List.map Stage.name |> String.concat "|"

let usage = $"Usage: build --stage {stageNames} | upload --container <uri>"

let private containerUri (value: string) =
  match Uri.TryCreate(value, UriKind.Absolute) with
  | true, uri when uri.Scheme = Uri.UriSchemeHttps -> Ok(Upload uri)
  | _ -> Error $"The container '{value}' is not an https URI. Give --container <uri>."

let parse (args: string list) : Result<Command, string> =
  match args with
  | [ "build"; "--stage"; value ] ->
    match Stage.parse value with
    | Some stage -> Ok(Build stage)
    | None -> Error $"Unknown stage '{value}'. Give --stage {stageNames}."
  | [ "build" ]
  | [ "build"; "--stage" ] -> Error $"build needs a stage. Give --stage {stageNames}."
  | [ "upload"; "--container"; value ] -> containerUri value
  | [ "upload" ]
  | [ "upload"; "--container" ] -> Error "upload needs a container. Give --container <uri>."
  | _ -> Error usage
