module TwoPoint.Core.Tests.PhotoMetadataTests

open TwoPoint.Core.Posts

open Expecto

open System.Text

module PhotoFixtures =

  let ascii (text : string) = Encoding.ASCII.GetBytes text

  let be16 (v : int) = [| byte (v >>> 8); byte v |]
  let le16 (v : int) = [| byte v; byte (v >>> 8) |]
  let be32 (v : int) = [| byte (v >>> 24); byte (v >>> 16); byte (v >>> 8); byte v |]
  let le32 (v : int) = [| byte v; byte (v >>> 8); byte (v >>> 16); byte (v >>> 24) |]

  let private entry (u16 : int -> byte[]) (u32 : int -> byte[]) tag typ count (value : byte[]) =
    Array.concat [ u16 tag; u16 typ; u32 count; value ]

  let tiff (bigEndian : bool) (orientation : int option) =
    let u16, u32 = if bigEndian then be16, be32 else le16, le32
    let header = Array.concat [ ascii (if bigEndian then "MM" else "II"); u16 42; u32 8 ]
    let entryCount = if orientation.IsSome then 4 else 3
    let ifdSize = 2 + 12 * entryCount + 4
    let make = ascii "Canon\000"
    let model = ascii "EOS 5D\000"
    let makeAt = 8 + ifdSize
    let modelAt = makeAt + make.Length
    let gpsAt = modelAt + model.Length + 1

    let ifd0 =
      Array.concat [
        u16 entryCount
        entry u16 u32 0x010F 2 make.Length (u32 makeAt)
        entry u16 u32 0x0110 2 model.Length (u32 modelAt)
        match orientation with
        | Some value -> entry u16 u32 0x0112 3 1 (Array.append (u16 value) [| 0uy; 0uy |])
        | None -> [||]
        entry u16 u32 0x8825 4 1 (u32 gpsAt)
        u32 0
      ]

    let gps =
      Array.concat [
        u16 2
        entry u16 u32 0x0001 2 2 (ascii "N\000\000\000")
        entry u16 u32 0x0002 5 3 (u32 (gpsAt + 30))
        u32 0
        Array.init 24 (fun i -> byte (0x40 + i))
      ]

    Array.concat [ header; ifd0; make; model; [| 0uy |]; gps ]

  let orientationTiff (value : int) =
    Array.concat [ ascii "MM"; be16 42; be32 8; be16 1; be16 0x0112; be16 3; be32 1; be16 value; be16 0; be32 0 ]

  let segment (marker : int) (payload : byte[]) =
    Array.concat [ [| 0xFFuy; byte marker |]; be16 (payload.Length + 2); payload ]

  let soi = [| 0xFFuy; 0xD8uy |]
  let eoi = [| 0xFFuy; 0xD9uy |]
  let jfif = segment 0xE0 (Array.append (ascii "JFIF\000") [| 1uy; 1uy; 0uy; 0uy; 1uy; 0uy; 1uy; 0uy; 0uy |])
  let icc = segment 0xE2 (Array.append (ascii "ICC_PROFILE\000") [| 1uy; 1uy; 9uy; 8uy; 7uy |])
  let exifPayload (tiffBytes : byte[]) = Array.append (ascii "Exif\000\000") tiffBytes

  let xmpPayload =
    ascii "http://ns.adobe.com/xap/1.0/\000<x:xmpmeta><tiff:Make>Canon</tiff:Make></x:xmpmeta>"

  let photoshopPayload =
    Array.concat [ ascii "Photoshop 3.0\000"; ascii "8BIM"; [| 4uy; 4uy; 0uy; 0uy; 1uy; 2uy; 3uy; 4uy |] ]

  let jpegImageData =
    Array.concat [
      segment 0xDB (Array.init 65 byte)
      segment 0xC0 [| 8uy; 0uy; 2uy; 0uy; 2uy; 1uy; 1uy; 0x11uy; 0uy |]
      segment 0xC4 (Array.init 20 (fun i -> byte (i * 3)))
      segment 0xDA [| 1uy; 1uy; 0uy; 0uy; 63uy; 0uy |]
      [| 0x12uy; 0xFFuy; 0x00uy; 0x34uy; 0xFFuy; 0xD0uy; 0x56uy; 0xFFuy; 0x00uy; 0x78uy |]
    ]

  let jpegWith (metadata : byte[] list) =
    Array.concat [ soi; jfif; Array.concat metadata; icc; jpegImageData; eoi ]

  let jpegWithMetadata (orientation : int option) =
    jpegWith [
      segment 0xE1 (exifPayload (tiff true orientation))
      segment 0xE1 xmpPayload
      segment 0xED photoshopPayload
    ]

  let cleanJpeg = jpegWith []

  let jpegWithOrientationOnly (value : int) =
    jpegWith [ segment 0xE1 (exifPayload (orientationTiff value)) ]

  let pngSignature = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]

  let crc32 (bytes : byte[]) =
    let mutable crc = 0xFFFFFFFFu

    for b in bytes do
      crc <- crc ^^^ uint32 b

      for _ in 1..8 do
        crc <- if crc &&& 1u = 1u then (crc >>> 1) ^^^ 0xEDB88320u else crc >>> 1

    crc ^^^ 0xFFFFFFFFu

  let chunk (chunkType : string) (data : byte[]) =
    let body = Array.append (ascii chunkType) data
    Array.concat [ be32 data.Length; body; be32 (int (crc32 body)) ]

  let ihdr = chunk "IHDR" (Array.concat [ be32 2; be32 2; [| 8uy; 2uy; 0uy; 0uy; 0uy |] ])
  let idat = [ chunk "IDAT" [| 1uy; 2uy; 3uy; 4uy |]; chunk "IDAT" [| 5uy; 6uy |] ]
  let iend = chunk "IEND" [||]
  let title = chunk "tEXt" (ascii "Title\000Sunset")

  let pngMetadata (orientation : int option) =
    [ chunk "eXIf" (tiff false orientation)
      chunk "iTXt" (Array.append (ascii "XML:com.adobe.xmp\000\000\000\000\000") (ascii "<x:xmpmeta>Canon</x:xmpmeta>"))
      title
      chunk "zTXt" (Array.append (ascii "Raw profile type exif\000\000") [| 0x78uy; 0x9Cuy; 1uy; 2uy |])
      chunk "tEXt" (ascii "Raw profile type iptc\000\nIPTC\n 4 1c02") ]

  let pngFrom (metadata : byte[] list) =
    Array.concat [ pngSignature; ihdr; Array.concat metadata; Array.concat idat; iend ]

  let cleanPng = pngFrom []

  let riffChunk (fourCc : string) (data : byte[]) =
    Array.concat [ ascii fourCc; le32 data.Length; data; (if data.Length % 2 = 1 then [| 0uy |] else [||]) ]

  let riff (chunks : byte[] list) =
    let body = Array.append (ascii "WEBP") (Array.concat chunks)
    Array.concat [ ascii "RIFF"; le32 body.Length; body ]

  let vp8x (flags : int) =
    riffChunk "VP8X" (Array.concat [ [| byte flags; 0uy; 0uy; 0uy |]; [| 1uy; 0uy; 0uy |]; [| 1uy; 0uy; 0uy |] ])

  let alph = riffChunk "ALPH" [| 1uy; 2uy; 3uy; 4uy; 5uy |]
  let vp8 = riffChunk "VP8 " (Array.init 12 (fun i -> byte (i + 20)))
  let iccp = riffChunk "ICCP" [| 9uy; 9uy |]
  let simpleWebP = riff [ vp8 ]

  let webpWithMetadata (orientation : int option) =
    riff [
      vp8x 0x1C
      iccp
      alph
      vp8
      riffChunk "EXIF" (tiff true orientation)
      riffChunk "XMP " (ascii "<x:xmpmeta>Canon</x:xmpmeta>")
    ]

open PhotoFixtures

let private strip format bytes =
  match PhotoMetadata.strip format bytes with
  | Ok stripped -> stripped
  | Error error -> failtestf "Expected stripped bytes, got %A" error

let private indexOf (needle : byte[]) (haystack : byte[]) =
  seq { 0 .. haystack.Length - needle.Length }
  |> Seq.tryFind (fun i -> haystack[i .. i + needle.Length - 1] = needle)

let private expectNoPrivateText (stripped : byte[]) =
  for text in [ "Canon"; "EOS 5D"; "xmpmeta"; "ns.adobe.com"; "Photoshop"; "8BIM"; "IPTC"; "Raw profile" ] do
    Expect.isNone (indexOf (ascii text) stripped) $"The output has no '{text}'"

let private expectRefused format bytes =
  match PhotoMetadata.strip format bytes with
  | Error _ -> ()
  | Ok _ -> failtest "Expected a refusal"

[<Tests>]
let tests =
  testList "Strip photo metadata" [
    testList "JPEG" [
      test "the output has only the Orientation EXIF and no GPS, camera, XMP, or IPTC data" {
        let stripped = strip PhotoFormat.Jpeg (jpegWithMetadata (Some 6))
        Expect.equal stripped (jpegWithOrientationOnly 6) "Same segments, with one minimal EXIF"
        expectNoPrivateText stripped
      }

      test "a little-endian EXIF keeps its Orientation" {
        let input = jpegWith [ segment 0xE1 (exifPayload (tiff false (Some 3))) ]
        Expect.equal (strip PhotoFormat.Jpeg input) (jpegWithOrientationOnly 3) "Orientation 3"
      }

      test "a file with no Orientation gets no EXIF" {
        Expect.equal (strip PhotoFormat.Jpeg (jpegWithMetadata None)) cleanJpeg "No EXIF"
      }

      test "the compressed image data is byte for byte the same" {
        let stripped = strip PhotoFormat.Jpeg (jpegWithMetadata (Some 8))
        Expect.isSome (indexOf jpegImageData stripped) "Tables, frame header, and scan are unchanged"
      }

      test "a file with no metadata is unchanged" {
        Expect.equal (strip PhotoFormat.Jpeg cleanJpeg) cleanJpeg "Unchanged"
      }

      test "data after the end of image is removed" {
        Expect.equal (strip PhotoFormat.Jpeg (Array.append cleanJpeg (ascii "Canon trailer"))) cleanJpeg "No trailer"
      }

      test "a damaged file is refused" {
        expectRefused PhotoFormat.Jpeg (Array.truncate 40 (jpegWithMetadata (Some 6)))
        expectRefused PhotoFormat.Jpeg (Array.append soi (ascii "no markers here"))
        expectRefused PhotoFormat.Jpeg (Array.append [| 0uy |] cleanJpeg)
        expectRefused PhotoFormat.Jpeg (Array.truncate (cleanJpeg.Length - 3) cleanJpeg)
      }
    ]

    testList "PNG" [
      test "the output has only the Orientation eXIf and no GPS, camera, XMP, or IPTC data" {
        let stripped = strip PhotoFormat.Png (pngFrom (pngMetadata (Some 6)))
        let expected = pngFrom [ title; chunk "eXIf" (orientationTiff 6) ]
        Expect.equal stripped expected "Other chunks stay, with a minimal eXIf before the first IDAT"
        expectNoPrivateText stripped
      }

      test "a file with no Orientation gets no eXIf" {
        Expect.equal (strip PhotoFormat.Png (pngFrom (pngMetadata None))) (pngFrom [ title ]) "No eXIf"
      }

      test "the compressed image data is byte for byte the same" {
        let stripped = strip PhotoFormat.Png (pngFrom (pngMetadata (Some 2)))
        Expect.isSome (indexOf (Array.concat idat) stripped) "The IDAT chunks are unchanged"
        Expect.isSome (indexOf ihdr stripped) "The IHDR chunk is unchanged"
      }

      test "a file with no metadata is unchanged" {
        Expect.equal (strip PhotoFormat.Png cleanPng) cleanPng "Unchanged"
      }

      test "a damaged file is refused" {
        expectRefused PhotoFormat.Png (Array.truncate (cleanPng.Length - 5) cleanPng)
        expectRefused PhotoFormat.Png (Array.concat [ pngSignature; ihdr; be32 99999; ascii "IDAT" ])
        expectRefused PhotoFormat.Png (Array.append [| 0uy |] cleanPng)
      }
    ]

    testList "WebP" [
      test "the output has only the Orientation EXIF and no GPS, camera, XMP, or IPTC data" {
        let stripped = strip PhotoFormat.WebP (webpWithMetadata (Some 6))
        let expected = riff [ vp8x 0x18; iccp; alph; vp8; riffChunk "EXIF" (orientationTiff 6) ]
        Expect.equal stripped expected "The flags match the chunks, and the RIFF size is right"
        expectNoPrivateText stripped
      }

      test "a file with no Orientation loses the EXIF and the XMP flags" {
        Expect.equal (strip PhotoFormat.WebP (webpWithMetadata None)) (riff [ vp8x 0x10; iccp; alph; vp8 ]) "Only the alpha flag stays"
      }

      test "the compressed image data is byte for byte the same" {
        let stripped = strip PhotoFormat.WebP (webpWithMetadata (Some 5))
        Expect.isSome (indexOf (Array.append alph vp8) stripped) "The ALPH and VP8 chunks are unchanged"
      }

      test "a simple file is unchanged, and data after the RIFF is removed" {
        Expect.equal (strip PhotoFormat.WebP simpleWebP) simpleWebP "Unchanged"
        Expect.equal (strip PhotoFormat.WebP (Array.append simpleWebP (ascii "tail"))) simpleWebP "No tail"
      }

      test "a damaged file is refused" {
        expectRefused PhotoFormat.WebP (Array.truncate (simpleWebP.Length - 3) simpleWebP)
        expectRefused PhotoFormat.WebP (Array.append (ascii "RIFX") (Array.skip 4 simpleWebP))
        expectRefused PhotoFormat.WebP (riff [ riffChunk "VP8X" [| 1uy |] ])
      }
    ]
  ]
