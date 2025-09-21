namespace TwoPoint.Core.Util

open FsToolkit.ErrorHandling

open System

// Top-level error codes that can occur within dependencies of the system
// Could be made a DU with nested enums in the future if that level of granularity is needed
type Dependency =
  | Database = 1
  | KeyVault = 2

type DependencyErrorType =
  | Validation of message: string
  | Unknown of Exception

/// <summary>
/// Details about an error in a dependency
/// </summary>
type DependencyError =
  { Dependency : Dependency
    Type : DependencyErrorType }
  
  member this.DebugMessage =
    match this.Type with
    | Validation msg -> msg
    | Unknown e      -> $"An unknown error occurred (message: {e.Message})"
  
  member this.Message =
    match this.Type with
    | Validation msg -> msg
    | Unknown _      -> $"An unknown error occurred (code: {this.Dependency |> int})"
    
  override this.ToString() = this.Message
  
module DependencyError =
  
  /// <summary>
  /// Convert a <c>Validation&lt;'ok, 'error&gt;</c> to a <c>DependencyError</c>
  /// </summary>
  /// <remarks>
  /// This function produces an error with <c>DependencyErrorType.Unknown</c>, rather than <c>DependencyErrorType.Validation</c>
  /// <c>DependencyErrorType.Validation</c> is intended as a means of propagating actual validation errors that the user
  /// can and must fix, whereas this method propagates "internal" validation errors (bad data, for example) which should be treated
  /// as an internal error
  /// </remarks>
  /// <param name="dep">The dependency the error occurred in</param>
  /// <param name="v">The validation to convert</param>
  let inline ofValidation dep (v: Validation<'ok, 'error>) =
    v
    |> Validation.mapError (fun err -> err.ToString() |> Option.ofObj |> Option.defaultValue "Unknown error")
    |> Result.mapError (fun errors -> String.Join(", ", errors))
    |> Result.mapError (fun aggregatedErrors -> { Dependency = dep; Type = $"The following validation errors occurred: {aggregatedErrors}" |> exn |> Unknown })
  
/// <summary>
/// The result of a dependency interaction (i.e. database or web service call, etc.)
/// </summary>
type DependencyResult<'T> = CancellableTaskResult<'T, DependencyError>


/// <summary>
/// An error that occurred during a PorchLight action execution
/// </summary>
type ActionError<'logicError> =
  | Dependency of DependencyError
  | Logic of 'logicError
  | Validation of string list
  
  override this.ToString() =
    match this with
    | Dependency dep -> dep.Message
    | Validation validation -> $"{validation}"
    | Logic logic -> $"{logic}"

/// <summary>
/// The result of a PorchLight action execution.
/// The <c>Error</c> case indicates that some error occurred interacting with a PorchLight dependency
/// The <c>Ok</c> case indicates that the business logic successfully executed and produced a result.
/// </summary>
type ActionResult<'success, 'logicError> = Result<'success, ActionError<'logicError>>

/// <summary>
/// This module type aliases useful functions defined in the <c>Validation</c> module for the ApiResult DSL
/// </summary>
module ActionResult =

  let inline success (x: 'success) : ActionResult<'success, 'logicError> = Ok x

  let inline failure x : ActionResult<'success, 'logicError> = Error x
  
  
module DependencyResult =
  
  let toActionResult eventCtor =
    Result.mapError ActionError.Dependency
    >>
    Result.either
      (eventCtor >> ActionResult.success)
      ActionResult.failure


/// <summary>
/// An error that occurred during a PorchLight query execution
/// </summary>
type QueryError =
  | Dependency of DependencyError
  | Validation of string list
  
  override this.ToString() =
    match this with
    | Dependency dep -> dep.Message
    | Validation validation ->
      let errors = validation |> String.concat ", "
      $"One or more validation errors occurred: {errors}"

/// <summary>
/// The result of a PorchLight query execution.
/// The <c>Error</c> case indicates that some error occurred interacting with a PorchLight dependency
/// The <c>Ok</c> case indicates that the query successfully executed and produced a result.
/// </summary>
type QueryResult<'success> = Result<'success, QueryError>

/// <summary>
/// This module type aliases useful functions defined in the <c>Validation</c> module for the QueryResult DSL
/// </summary>
module QueryResult =

  let inline success (x: 'success) : QueryResult<'success> = Ok x

  let inline failure x : QueryResult<'success> = Error x
