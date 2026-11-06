namespace TwoPoint.Dependencies.Util

open System

module Base64Url =

  let encode (bytes : byte array) =
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

  let decode (value : string) =
    let padded = value.Replace('-', '+').Replace('_', '/')
    Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='))
