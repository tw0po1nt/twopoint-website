namespace TwoPoint.Http.Extensions

open TwoPoint.Core.Deploys
open TwoPoint.Core.Shared

module Bind =
  open Symbolica.Extensions.Configuration.FSharp
  open System

  let guid = Bind.tryParseable Guid.TryParse

  let bool = Bind.tryParseable Boolean.TryParse

  let emailAddress = Bind.tryParseable EmailAddress.tryParse

  let repo = Bind.tryParseable Repo.tryParse
