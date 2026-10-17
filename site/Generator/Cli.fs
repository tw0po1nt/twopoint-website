[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Cli

type Command = Build of Stage

let private stageNames = Stage.all |> List.map Stage.name |> String.concat "|"

let usage = $"Usage: build --stage {stageNames}"

let parse (args: string list) : Result<Command, string> =
  match args with
  | [ "build"; "--stage"; value ] ->
    match Stage.parse value with
    | Some stage -> Ok(Build stage)
    | None -> Error $"Unknown stage '{value}'. Give --stage {stageNames}."
  | [ "build" ]
  | [ "build"; "--stage" ] -> Error $"build needs a stage. Give --stage {stageNames}."
  | _ -> Error usage
