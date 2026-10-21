namespace TwoPoint.Site.Generator

open Azure
open Azure.Identity
open System

[<RequireQualifiedAccess>]
module AzureRequest =

  let attempt (action: unit -> 'T) =
    try
      Ok(action ())
    with
    | :? AuthenticationFailedException as error -> Error error.Message
    | :? RequestFailedException as error -> Error error.Message
    // The retry policy wraps a network failure after its last retry.
    | :? AggregateException as error -> Error error.Message
