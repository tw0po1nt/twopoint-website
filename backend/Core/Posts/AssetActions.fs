namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Security.Cryptography
open System.Text

// ====================================================================================================================
// Upload asset
// ====================================================================================================================

[<RequireQualifiedAccess>]
module UploadAsset =

  type Dto = { Bytes : byte[] }

  type Kind =
    | Jpeg
    | Png
    | WebP
    | Gif
    | Pdf

  type Error =
    | UnsupportedType
    | TooLarge

  type Event = Uploaded of name: string * url: Uri

  [<RequireQualifiedAccess>]
  module Logic =

    let maxBytes = 25 * 1024 * 1024

    let private startsWith (prefix : byte[]) (offset : int) (bytes : byte[]) =
      bytes.Length >= offset + prefix.Length && bytes[offset .. offset + prefix.Length - 1] = prefix

    let private ascii (text : string) = Encoding.ASCII.GetBytes text

    let kind (bytes : byte[]) : Kind option =
      if bytes |> startsWith [| 0xFFuy; 0xD8uy; 0xFFuy |] 0 then Some Jpeg
      elif bytes |> startsWith [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |] 0 then Some Png
      elif bytes |> startsWith (ascii "GIF87a") 0 || bytes |> startsWith (ascii "GIF89a") 0 then Some Gif
      elif bytes |> startsWith (ascii "%PDF-") 0 then Some Pdf
      elif bytes |> startsWith (ascii "RIFF") 0 && bytes |> startsWith (ascii "WEBP") 8 then Some WebP
      else None

    type Format = { Extension : string; ContentType : string }

    let format = function
      | Jpeg -> { Extension = "jpg"; ContentType = "image/jpeg" }
      | Png -> { Extension = "png"; ContentType = "image/png" }
      | WebP -> { Extension = "webp"; ContentType = "image/webp" }
      | Gif -> { Extension = "gif"; ContentType = "image/gif" }
      | Pdf -> { Extension = "pdf"; ContentType = "application/pdf" }

    let blobName (kind : Kind) (bytes : byte[]) =
      let hash = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
      $"{hash.Substring(0, 16)}.{(format kind).Extension}"

    let decide (bytes : byte[]) : Decision<Kind, Error> =
      match kind bytes with
      | None -> Decision.failure UnsupportedType
      | Some _ when bytes.Length > maxBytes -> Decision.failure TooLarge
      | Some kind -> Decision.success kind

  type Dependencies =
    /// A blob with the same name is not written again.
    abstract member WriteAsset: name: string -> contentType: string -> content: byte[] -> DependencyResult<Uri>

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let withDependencies (dependencies : Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! kind = Logic.decide dto.Bytes |> Result.mapError ActionError<Error>.Logic
      let name = Logic.blobName kind dto.Bytes

      let! url =
        dependencies.WriteAsset name (Logic.format kind).ContentType dto.Bytes
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      return Event.Uploaded(name, url)
    }
