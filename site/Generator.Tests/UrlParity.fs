/// The URL list contains each public path of the Astro build. The pending list contains the paths
/// of the URL list that the generator does not write yet.
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

/// Gives one error for each listed path that is missing and not pending, and one error for each pending
/// path that is in the output.
let check (urls: Set<string>) (pending: Set<string>) (output: Set<string>) =
  let missing =
    Set.difference (Set.difference urls pending) output
    |> Seq.map (fun path -> $"The output has no '{path}'.")

  let written =
    Set.intersect pending output
    |> Seq.map (fun path -> $"The output has '{path}'. Remove it from the pending list.")

  Seq.append missing written |> List.ofSeq
