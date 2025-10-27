namespace TwoPoint.Http.Extensions

open TwoPoint.Core.Shared

module Bind =
  open Symbolica.Extensions.Configuration.FSharp
  open System

  let guid = Bind.tryParseable Guid.TryParse
  
  let emailAddress = Bind.tryParseable EmailAddress.tryParse

