namespace TwoPoint.Site.Generator

open Azure.Identity
open Azure.Storage.Blobs
open FsToolkit.ErrorHandling
open System
open System.IO

type IPostStore =
  /// Gives the name and the bytes of each blob in the container.
  abstract Download: container: string -> Result<(string * byte array) list, string>

[<RequireQualifiedAccess>]
module PostStore =

  let azure (account: string) =
    let service =
      BlobServiceClient(Uri $"https://{account}.blob.core.windows.net", DefaultAzureCredential())

    { new IPostStore with
        member _.Download container =
          BlobContainer.attempt (fun () ->
            let client = service.GetBlobContainerClient container

            [ for item in client.GetBlobs() do
                item.Name, client.GetBlobClient(item.Name).DownloadContent().Value.Content.ToArray() ]) }

[<RequireQualifiedAccess>]
module Pull =

  let private download (store: IPostStore) (container: string) (extension: string) =
    let file (name: string, bytes) =
      if Path.GetFileName name = name && name.EndsWith extension && name.Length > extension.Length then
        Ok
          { Path = PostFolder.filePath PostFolder.pulled container name
            Bytes = bytes }
      else
        Error $"The blob '{name}' in '{container}' is not a {extension} file at the top of the container."

    store.Download container
    |> Result.mapError (fun reason -> $"Cannot download the container '{container}': {reason}")
    |> Result.bind (List.traverseResultM file)
    |> Result.map (List.sortBy _.Path)

  /// Gives each file at its path in the pulled folder.
  let run (store: IPostStore) : Result<PostFiles, string> =
    result {
      let! published = download store PostFolder.published ".md"
      let! series = download store PostFolder.series ".yml"
      return { Published = published; Series = series }
    }
