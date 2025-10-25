namespace System

open System.Text.Json

[<AutoOpen>]
module JsonExtensions =
  
  type BinaryData with

    member this.FromJson<'T when 'T : null>() =
      let options = JsonSerializerOptions()
      options.PropertyNameCaseInsensitive <- true
      this.ToObjectFromJson<'T>(options)
    
    
  
  

