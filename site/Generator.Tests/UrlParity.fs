module TwoPoint.Site.Generator.Tests.UrlParity

open System.IO
open System.Reflection

let private readList name =
  use stream = Assembly.GetExecutingAssembly().GetManifestResourceStream name
  use reader = new StreamReader(stream)

  reader.ReadToEnd().Split('\n')
  |> Array.map _.Trim()
  |> Array.filter (fun line -> line <> "")
  |> Set.ofArray

let urls = readList "urls.txt"

let pending = readList "pending-urls.txt"

let checkWith (pending: Set<string>) (output: Set<string>) =
  let missing =
    Set.difference (Set.difference urls pending) output
    |> Seq.map (fun path -> $"The output has no '{path}'.")

  let written =
    Set.intersect pending output
    |> Seq.map (fun path -> $"The output has '{path}'. Remove it from the pending list.")

  Seq.append missing written |> List.ofSeq

let check = checkWith pending
