namespace TwoPoint.Http.Extensions

module Bind =
  open Symbolica.Extensions.Configuration.FSharp
  open System

  let guid = Bind.tryParseable Guid.TryParse

