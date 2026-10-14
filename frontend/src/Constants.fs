[<RequireQualifiedAccess>]
module Constants

open System.Text.RegularExpressions

let emailRegex = Regex "^\w+([-+.']\w+)*@(\[*\w+)([-.]\w+)*\.\w+([-.]\w+\])*$"

