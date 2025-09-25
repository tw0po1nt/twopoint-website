namespace TwoPoint.Core.Util

[<RequireQualifiedAccess>]
module List =
  
  let defaultIfEmpty defaultValue = function
    | [] -> defaultValue
    | lst -> lst
