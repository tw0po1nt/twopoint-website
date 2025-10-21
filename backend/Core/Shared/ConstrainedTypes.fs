namespace TwoPoint.Core.Shared

open TwoPoint.Core.Util

open FsToolkit.ErrorHandling

module Types =
  
  // Helper functions

  let unsafeCreate (create: 'a -> Validation<'b, string>) value =
    let result = create value
    try
      result
      |> Result.toOption
      |> Option.get
    with
      | :? System.ArgumentException ->
        let errors = result |> Result.defaultError ["An unknown error occurred"]
        let msg = System.String.Join(", ", errors)
        System.ArgumentException msg |> raise

  let createMaybe create maybeValue =
    maybeValue
    |> Option.traverseResult create

module Constrained =
  
  let private atLeast value magnitude onLt min  =
    if magnitude value < min
    then Error onLt
    else Ok value
    
  let private atMost value magnitude onGt max =
    if magnitude value > max
    then Error onGt
    else Ok value

  module String =
    
    let between min max fieldName ctor (value: string) = validation {
      let pluralize plurality word =
        if plurality = 1 then word else $"{word}s"
      
      let errMsg =
        match min, max with
        | Some min, Some max ->
          $"{fieldName} must be between {min} and {max} characters long"
        | Some min, _ ->
          let pluralization = pluralize min "character"
          $"{fieldName} must be at least {min} {pluralization} long"
        | _, Some max ->
          let pluralization = pluralize max "character"
          $"{fieldName} must be at most {max} {pluralization} long"
        | None, None ->
          failwith $"{nameof String}.between requires a min or a max. For an unconstrained string, just use a built-in string"
        
      do!
        min
        |> Option.traverseResult (atLeast value (konst value.Length) errMsg)
        |> Result.ignore
        
      do!
        max
        |> Option.traverseResult (atMost value (konst value.Length) errMsg)
        |> Result.ignore
        
      return ctor value
    }
    
    let tryParse parser onError ctor value: Validation<'c, 'b> =
      value
      |> parser
      |> Validation.requireSome onError
      |> Validation.map ctor

  
  module UInt =
    open System
    
    let between min max fieldName ctor value = validation {
      let errMsg =
        let mustBeWhole = "must be a whole number"
        match min, max with
        | Some min, Some max -> $"{fieldName} {mustBeWhole} between {min} and {max}"
        | Some min, _        -> $"{fieldName} {mustBeWhole} and at least {min}"
        | _, Some max        -> $"{fieldName} {mustBeWhole} and at most {max}"
        | None, None         -> failwith $"{nameof UInt32}.between requires a min or a max. For an unconstrained uint, just use a built-in uint"
      
      let! value =
        value
        |> UInt32.TryParseOption
        |> Validation.requireSome errMsg
      
      do!
        min
        |> Option.traverseResult (atLeast value (konst value) errMsg)
        |> Result.ignore
        
      do!
        max
        |> Option.traverseResult (atMost value (konst value) errMsg)
        |> Result.ignore
        
      return ctor value
    }
