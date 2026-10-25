namespace TwoPoint.Core.Posts

open TwoPoint.Core.Shared

open FsToolkit.ErrorHandling

open System

type UserExternalId = 
  private | UserExternalId of Guid

  override this.ToString() =
    let (UserExternalId ueid) = this
    ueid.ToString()
    
module UserExternalId =
  let create =
    Constrained.String.tryParse
      Guid.TryParseOption
      $"'{nameof UserExternalId}' must be a valid guid"
      UserExternalId
      
  let createMaybe = Option.traverseResult create

  module Unsafe =
    let create = Types.unsafeCreate create
