namespace TwoPoint.Core.Shared

open FsToolkit.ErrorHandling

open TwoPoint.Core.Util

type NonEmptyString =
  private | NonEmptyString of string
  
  override this.ToString() =
    let (NonEmptyString nes) = this
    nes

module NonEmptyString =
  let create fieldName =
    let field = fieldName |> Option.defaultValue (nameof NonEmptyString)
    Constrained.String.between (Some 1) None field NonEmptyString
    
  let createMaybe fieldName = Option.traverseResult (create fieldName)
  
  let value (NonEmptyString nes) = nes
  
  module Unsafe =
    let create fieldName = Types.unsafeCreate (create fieldName)


type EmailAddress =
  private | EmailAddress of string
  
  override this.ToString() =
    let (EmailAddress email) = this
    email

module EmailAddress =
  
  let create =
    Constrained.String.tryParse
      (regexParser @"^\w+([-+.']\w+)*@(\[*\w+)([-.]\w+)*\.\w+([-.]\w+\])*$")
      "Email address is invalid"
      EmailAddress
      
  let createMaybe = Option.traverseResult create
    
  let value (EmailAddress ea) = ea
  
  module Unsafe =
    let create = Types.unsafeCreate create
    
    
[<AutoOpen>]
module StringTypesActivePatterns =
  
  let private mapSomeDefaultNone x = (Result.map Some >> Result.defaultValue None) x
  
  let (|NonEmptyString|_|) = NonEmptyString.create None >> mapSomeDefaultNone
    
  let (|EmailAddress|_|) = EmailAddress.create >> mapSomeDefaultNone  
