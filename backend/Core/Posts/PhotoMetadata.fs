namespace TwoPoint.Core.Posts

open System
open System.Text

[<RequireQualifiedAccess>]
type PhotoFormat =
  | Jpeg
  | Png
  | WebP

[<RequireQualifiedAccess>]
type MalformedPhoto =
  | Truncated
  | InvalidStructure

[<RequireQualifiedAccess>]
module PhotoMetadata =

  let private ascii (text : string) = Encoding.ASCII.GetBytes text

  let private startsWith (prefix : byte[]) (offset : int) (bytes : byte[]) =
    bytes.Length >= offset + prefix.Length && bytes[offset .. offset + prefix.Length - 1] = prefix

  let private be16 (b : byte[]) o = (int b[o] <<< 8) ||| int b[o + 1]
  let private le16 (b : byte[]) o = (int b[o + 1] <<< 8) ||| int b[o]
  let private be32 (b : byte[]) o = (int64 (be16 b o) <<< 16) ||| int64 (be16 b (o + 2))
  let private le32 (b : byte[]) o = (int64 (le16 b (o + 2)) <<< 16) ||| int64 (le16 b o)

  let private putBe32 (value : int) =
    [| byte (value >>> 24); byte (value >>> 16); byte (value >>> 8); byte value |]

  let private putLe32 (value : int) =
    [| byte value; byte (value >>> 8); byte (value >>> 16); byte (value >>> 24) |]

  let private orientationTag = 0x0112

  /// EXIF defines Orientation values 1 to 8 only.
  let private tiffOrientation (tiff : byte[]) : int option =
    let reader =
      if tiff.Length < 8 then None
      elif tiff[0] = byte 'M' && tiff[1] = byte 'M' then Some(be16, be32)
      elif tiff[0] = byte 'I' && tiff[1] = byte 'I' then Some(le16, le32)
      else None

    match reader with
    | Some(u16, u32) when u16 tiff 2 = 42 ->
      let ifd = u32 tiff 4

      if ifd + 2L > int64 tiff.Length then
        None
      else
        let ifd = int ifd

        seq { 0 .. u16 tiff ifd - 1 }
        |> Seq.map (fun i -> ifd + 2 + 12 * i)
        |> Seq.takeWhile (fun entry -> entry + 12 <= tiff.Length)
        |> Seq.tryPick (fun entry ->
          let value = u16 tiff (entry + 8)

          if u16 tiff entry = orientationTag && u16 tiff (entry + 2) = 3 && u32 tiff (entry + 4) = 1L
             && value >= 1 && value <= 8 then
            Some value
          else
            None)
    | _ -> None

  let private exifPrefix = ascii "Exif\000\000"

  let private withoutExifPrefix (bytes : byte[]) =
    if bytes |> startsWith exifPrefix 0 then bytes[exifPrefix.Length ..] else bytes

  let private orientationTiff (orientation : int) =
    [| byte 'M'; byte 'M'; 0uy; 42uy; 0uy; 0uy; 0uy; 8uy
       0uy; 1uy
       0x01uy; 0x12uy; 0uy; 3uy; 0uy; 0uy; 0uy; 1uy; 0uy; byte orientation; 0uy; 0uy
       0uy; 0uy; 0uy; 0uy |]

  // ==================================================================================================================
  // JPEG
  // ==================================================================================================================

  type private Segment = { Marker : int; Bytes : byte[] }

  let private isStandalone marker = marker = 0x01 || (marker >= 0xD0 && marker <= 0xD8)

  let rec private scanEnd (bytes : byte[]) (i : int) : Result<int, MalformedPhoto> =
    if i + 1 >= bytes.Length then Error MalformedPhoto.Truncated
    elif bytes[i] <> 0xFFuy then scanEnd bytes (i + 1)
    else
      match int bytes[i + 1] with
      | 0x00 -> scanEnd bytes (i + 2)
      | marker when marker >= 0xD0 && marker <= 0xD7 -> scanEnd bytes (i + 2)
      | 0xFF -> scanEnd bytes (i + 1)
      | _ -> Ok i

  let rec private readSegments (bytes : byte[]) (pos : int) (acc : Segment list) : Result<Segment list, MalformedPhoto> =
    if pos + 1 >= bytes.Length then Error MalformedPhoto.Truncated
    elif bytes[pos] <> 0xFFuy then
      match Array.IndexOf(bytes, 0xFFuy, pos) with
      | -1 -> Error MalformedPhoto.Truncated
      | next -> readSegments bytes next acc
    elif bytes[pos + 1] = 0xFFuy then readSegments bytes (pos + 1) acc
    else
      let marker = int bytes[pos + 1]

      if marker = 0xD9 then
        Ok(List.rev ({ Marker = marker; Bytes = bytes[pos .. pos + 1] } :: acc))
      elif isStandalone marker then
        readSegments bytes (pos + 2) ({ Marker = marker; Bytes = bytes[pos .. pos + 1] } :: acc)
      elif pos + 4 > bytes.Length then Error MalformedPhoto.Truncated
      else
        let length = be16 bytes (pos + 2)
        let headerEnd = pos + 2 + length

        if length < 2 then Error MalformedPhoto.InvalidStructure
        elif headerEnd > bytes.Length then Error MalformedPhoto.Truncated
        elif marker = 0xDA then
          match scanEnd bytes headerEnd with
          | Error error -> Error error
          | Ok finish -> readSegments bytes finish ({ Marker = marker; Bytes = bytes[pos .. finish - 1] } :: acc)
        else
          readSegments bytes headerEnd ({ Marker = marker; Bytes = bytes[pos .. headerEnd - 1] } :: acc)

  let private jpegOrientation (segment : Segment) =
    if segment.Marker = 0xE1 && segment.Bytes |> startsWith exifPrefix 4 then
      tiffOrientation segment.Bytes[4 + exifPrefix.Length ..]
    else
      None

  let private keepSegment (segment : Segment) =
    match segment.Marker with
    | 0xE0 -> true
    | 0xE2 -> segment.Bytes |> startsWith (ascii "ICC_PROFILE\000") 4
    | 0xEE -> segment.Bytes |> startsWith (ascii "Adobe") 4
    | marker -> marker < 0xE1 || marker > 0xEF && marker <> 0xFE

  let private jpegExif (orientation : int) =
    let tiff = orientationTiff orientation
    let length = 2 + exifPrefix.Length + tiff.Length
    { Marker = 0xE1; Bytes = Array.concat [ [| 0xFFuy; 0xE1uy; byte (length >>> 8); byte length |]; exifPrefix; tiff ] }

  let private stripJpeg (bytes : byte[]) : Result<byte[], MalformedPhoto> =
    if not (bytes |> startsWith [| 0xFFuy; 0xD8uy |] 0) then
      Error MalformedPhoto.InvalidStructure
    else
      readSegments bytes 2 [ { Marker = 0xD8; Bytes = bytes[0..1] } ]
      |> Result.map (fun segments ->
        let kept = segments |> List.filter keepSegment

        let withExif =
          match segments |> List.tryPick jpegOrientation with
          | None -> kept
          | Some orientation ->
            let at = match kept with _ :: { Marker = 0xE0 } :: _ -> 2 | _ -> 1
            List.insertAt at (jpegExif orientation) kept

        withExif |> List.map _.Bytes |> Array.concat)

  // ==================================================================================================================
  // PNG
  // ==================================================================================================================

  type private Chunk = { Type : string; Data : byte[]; Raw : byte[] }

  let private crcTable =
    Array.init 256 (fun n ->
      (n |> uint32, [ 1..8 ])
      ||> List.fold (fun c _ -> if c &&& 1u = 1u then 0xEDB88320u ^^^ (c >>> 1) else c >>> 1))

  let private crc32 (bytes : byte[]) =
    let crc = bytes |> Array.fold (fun c b -> crcTable[int ((c ^^^ uint32 b) &&& 0xFFu)] ^^^ (c >>> 8)) 0xFFFFFFFFu
    crc ^^^ 0xFFFFFFFFu

  let private pngChunk (chunkType : string) (data : byte[]) =
    let body = Array.append (ascii chunkType) data
    { Type = chunkType
      Data = data
      Raw = Array.concat [ putBe32 data.Length; body; putBe32 (int (crc32 body)) ] }

  let rec private readChunks (bytes : byte[]) (pos : int) (acc : Chunk list) : Result<Chunk list, MalformedPhoto> =
    if pos + 12 > bytes.Length then Error MalformedPhoto.Truncated
    else
      let length = be32 bytes pos

      if length > int64 (bytes.Length - pos - 12) then
        Error MalformedPhoto.Truncated
      else
        let finish = pos + 12 + int length
        let chunkType = Encoding.ASCII.GetString(bytes, pos + 4, 4)
        let chunk = { Type = chunkType; Data = bytes[pos + 8 .. finish - 5]; Raw = bytes[pos .. finish - 1] }

        if chunkType = "IEND" then Ok(List.rev (chunk :: acc)) else readChunks bytes finish (chunk :: acc)

  let private isPngMetadata (chunk : Chunk) =
    match chunk.Type with
    | "eXIf" | "tEXt" | "zTXt" | "iTXt" | "caBX" -> true
    | _ -> false

  let private pngSignature = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]

  let private stripPng (bytes : byte[]) : Result<byte[], MalformedPhoto> =
    if not (bytes |> startsWith pngSignature 0) then
      Error MalformedPhoto.InvalidStructure
    else
      readChunks bytes pngSignature.Length []
      |> Result.map (fun chunks ->
        let kept = chunks |> List.filter (isPngMetadata >> not)

        let withExif =
          match chunks |> List.tryFind (fun chunk -> chunk.Type = "eXIf") |> Option.bind (fun chunk -> tiffOrientation (withoutExifPrefix chunk.Data)) with
          | None -> kept
          | Some orientation ->
            let at = kept |> List.tryFindIndex (fun chunk -> chunk.Type = "IDAT") |> Option.defaultValue (kept.Length - 1)
            List.insertAt at (pngChunk "eXIf" (orientationTiff orientation)) kept

        Array.append pngSignature (withExif |> List.map _.Raw |> Array.concat))

  // ==================================================================================================================
  // WebP
  // ==================================================================================================================

  type private RiffChunk = { FourCc : string; Content : byte[] }

  let rec private readRiffChunks (bytes : byte[]) (pos : int) (finish : int) (acc : RiffChunk list) =
    if pos = finish then Ok(List.rev acc)
    elif pos + 8 > finish then Error MalformedPhoto.Truncated
    else
      let size = le32 bytes (pos + 4)
      let dataEnd = int64 pos + 8L + size
      let next = if dataEnd = int64 finish then dataEnd else dataEnd + (size &&& 1L)

      if next > int64 finish then
        Error MalformedPhoto.Truncated
      else
        let chunk = { FourCc = Encoding.ASCII.GetString(bytes, pos, 4); Content = bytes[pos + 8 .. pos + 7 + int size] }
        readRiffChunks bytes (int next) finish (chunk :: acc)

  let private riffBytes (chunk : RiffChunk) =
    let pad = if chunk.Content.Length % 2 = 1 then [| 0uy |] else [||]
    Array.concat [ ascii chunk.FourCc; putLe32 chunk.Content.Length; chunk.Content; pad ]

  let private riffFile (chunks : RiffChunk list) =
    let body = Array.concat [ ascii "WEBP"; chunks |> List.map riffBytes |> Array.concat ]
    Array.concat [ ascii "RIFF"; putLe32 body.Length; body ]

  let private isWebPMetadata (chunk : RiffChunk) = chunk.FourCc = "EXIF" || chunk.FourCc = "XMP "

  let private exifFlag = 0x08uy
  let private xmpFlag = 0x04uy

  let private stripWebP (bytes : byte[]) : Result<byte[], MalformedPhoto> =
    if bytes.Length < 12 || not (bytes |> startsWith (ascii "RIFF") 0) || not (bytes |> startsWith (ascii "WEBP") 8) then
      Error MalformedPhoto.InvalidStructure
    else
      let riffSize = le32 bytes 4

      if riffSize < 4L then Error MalformedPhoto.InvalidStructure
      elif riffSize + 8L > int64 bytes.Length then Error MalformedPhoto.Truncated
      else
        readRiffChunks bytes 12 (int riffSize + 8) []
        |> Result.bind (fun chunks ->
          match chunks with
          | { FourCc = "VP8X"; Content = header } :: _ when header.Length >= 10 ->
            let orientation =
              chunks
              |> List.tryFind (fun chunk -> chunk.FourCc = "EXIF")
              |> Option.bind (fun chunk -> tiffOrientation (withoutExifPrefix chunk.Content))

            let flags =
              (header[0] &&& ~~~(exifFlag ||| xmpFlag)) ||| (if orientation.IsSome then exifFlag else 0uy)

            let kept =
              chunks
              |> List.filter (isWebPMetadata >> not)
              |> List.mapi (fun i chunk ->
                if i = 0 then { chunk with Content = Array.append [| flags |] header[1..] } else chunk)

            let exif =
              orientation
              |> Option.map (fun value -> { FourCc = "EXIF"; Content = orientationTiff value })
              |> Option.toList

            Ok(riffFile (kept @ exif))
          | { FourCc = "VP8X" } :: _ -> Error MalformedPhoto.InvalidStructure
          | _ -> Ok(riffFile (chunks |> List.filter (isWebPMetadata >> not))))

  let strip (format : PhotoFormat) (bytes : byte[]) : Result<byte[], MalformedPhoto> =
    match format with
    | PhotoFormat.Jpeg -> stripJpeg bytes
    | PhotoFormat.Png -> stripPng bytes
    | PhotoFormat.WebP -> stripWebP bytes
