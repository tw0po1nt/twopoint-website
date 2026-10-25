namespace TwoPoint.Dependencies.Posts

open System.Collections.Concurrent

/// Keeps the text of each blob under the container, the name, and the ETag that the download returned.
type BlobTextCache() =

  let texts = ConcurrentDictionary<struct (string * string * string), string>()

  member _.TryFind (container : string) (name : string) (eTag : string) : string option =
    match texts.TryGetValue(struct (container, name, eTag)) with
    | true, text -> Some text
    | false, _ -> None

  member _.Add (container : string) (name : string) (eTag : string) (text : string) =
    texts[struct (container, name, eTag)] <- text

  member _.Retain (container : string) (current : Set<string * string>) =
    for struct (cachedContainer, name, eTag) as key in texts.Keys do
      if cachedContainer = container && not (current.Contains(name, eTag)) then
        texts.TryRemove key |> ignore
