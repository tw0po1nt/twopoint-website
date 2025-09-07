namespace System

[<AutoOpen>]
module NetExtensions =
  open TwoPoint.Core.Util
  
  open FsToolkit.ErrorHandling
  
  type DateTime with
  
    static member TryParseUtc value =
      match value with
      | DateTime parsed -> Some parsed
      | _ -> None
      
    static member TryValidateUtc (value, ?fieldName) =
      match value with
      | DateTime parsed -> Validation.ok parsed
      | _ ->
        let field = fieldName |> Option.defaultValue (nameof value)
        Validation.error $"'{field}' must be a valid UTC datetime"
  
  type Guid with
  
    static member TryParseOption value =
      match value with
      | Guid parsed -> Some parsed
      | _ -> None

  type UInt32 with
  
    static member TryParseOption value =
      match value with
      | UInt parsed -> Some parsed
      | _ -> None
      
    static member TryValidate (value, ?fieldName) =
      match value with
      | UInt parsed -> Validation.ok parsed
      | _ ->
        let field = fieldName |> Option.defaultValue (nameof value)
        Validation.error $"'{field}' must not be negative"
        
  type Uri with
  
    static member TryParseOption value =
      match value with
      | Uri parsed -> Some parsed
      | _ -> None
      
    static member TryValidate (value, ?fieldName) =
      match value with
      | Uri parsed -> Ok parsed
      | _ ->
        let field = fieldName |> Option.defaultValue (nameof value)
        Error $"'{field}' must be a valid URI"
