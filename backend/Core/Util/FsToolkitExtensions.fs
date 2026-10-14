namespace FsToolkit.ErrorHandling

open IcedTasks

module Option =
  
  /// <summary>
  /// Traverses an option value and applies a function to its inner value, returning a validation.
  /// </summary>
  /// <param name="binder">The function to apply to the inner value of the option.</param>
  /// <param name="input">The option value to traverse.</param>
  /// <returns>A validation containing either an option with the transformed value or an error.</returns>
  let inline traverseValidation
    ([<InlineIfLambda>] binder: 'input -> Validation<'okOutput, 'error>)
    (input: option<'input>)
    : Validation<'okOutput option, 'error> =
    match input with
    | None -> Ok None
    | Some v ->
      binder v
      |> Validation.map Some
  
  /// <summary>
  /// Converts an <c>Option&lt;CancellableTaskResult&lt;'success, 'error&gt;&gt;</c> to a <c>CancellableTaskResult&lt;'success option, 'error&gt;</c>.
  /// </summary>
  let inline sequenceCancellableTaskResult
    (optCancellableTaskRes: Option<CancellableTaskResult<'success, 'error>>)
    : CancellableTaskResult<'success option, 'error> =
    cancellableTask {
      match optCancellableTaskRes with
      | Some cancellableTaskRes ->
        let! cTaskRes = cancellableTaskRes
        return cTaskRes |> Result.map Some
      | None ->
        return Ok None
    }
    
  /// <summary>
  /// Maps a <c>CancellableTaskResult</c> function over an <c>option</c>, returning a <c>CancellableTaskResult&lt;'b option, 'error&gt;</c>.
  /// </summary>
  /// <param name="f">The function to map over the <c>option</c></param>
  /// <param name="opt">The <c>option</c> to map over</param>
  /// <returns>A <c>CancellableTaskResult</c> with the mapped value</returns>
  let inline traverseCancellableTaskResult
    ([<InlineIfLambda>] f: 'a -> CancellableTaskResult<'b, 'error>)
    (opt: Option<'a>)
    : CancellableTaskResult<'b option, 'error> =
    sequenceCancellableTaskResult (Option.map f opt)

module Result =
  /// <summary>
  /// Requires a value to be <c>None</c>, otherwise calls a function and returns its error result.
  /// </summary>
  /// <param name="onSome">The function to call to produce an error value to return if the value is <c>Some</c>.</param>
  /// <param name="option">The <c>Option</c> value to check.</param>
  /// <returns>An <c>Ok</c> result if the value is <c>None</c>, otherwise an Error result with the specified error value.</returns>
  let inline requireNoneWith (onSome: 'value -> 'error) (option: 'value option) : Result<unit, 'error> =
    match option with
    | Some value -> Error (onSome value)
    | None -> Ok()
    
  /// <summary>
  /// Converts a <c>Result&lt;CancellableTask&lt;'a&gt;, 'b&gt;</c> to a <c>CancellableTaskResult&lt;'a, 'b&gt;</c>.
  /// </summary> 
  let inline sequenceCancellableTask
    (resCancellableTask: Result<CancellableTask<'a>, 'b>)
    : CancellableTaskResult<'a, 'b> =
    cancellableTask {
      match resCancellableTask with
      | Ok cancellableTask ->
        let! cTaskRes = cancellableTask
        return Ok cTaskRes
      | Error err ->
        return Error err
    }

  /// <summary>
  /// Maps a <c>CancellableTask</c> function over a <c>Result</c>, returning a <c>CancellableTaskResult</c>.
  /// </summary>
  /// <param name="f">The function to map over the Result.</param>
  /// <param name="res">The Result to map over.</param>
  /// <returns>A <c>CancellableTaskResult</c> with the mapped value</returns>
  let inline traverseCancellableTask
    ([<InlineIfLambda>] f: 'a -> CancellableTask<'b>)
    (res: Result<'a, 'c>)
    : CancellableTaskResult<'b, 'c> =
    sequenceCancellableTask (Result.map f res)

module Validation =
  
  /// <summary>
  /// Requires a value to be <c>Some</c>, otherwise returns an error result.
  /// </summary>
  /// <param name="error">The error value to return if the value is <c>None</c>.</param>
  /// <param name="option">The <c>Option</c> value to check.</param>
  /// <returns>An <c>Ok</c> result if the value is <c>Some</c>, otherwise an Error result with the specified error value.</returns>
  let inline requireSome (error: 'error) (option: 'ok option) : Validation<'ok, 'error> =
      match option with
      | Some x -> Ok x
      | None -> Validation.error error
      
  /// <summary>
  /// Requires a value to be <c>None</c>, otherwise returns an error result.
  /// </summary>
  /// <param name="error">The error value to return if the value is <c>Some</c>.</param>
  /// <param name="option">The <c>Option</c> value to check.</param>
  /// <returns>An <c>Ok</c> result if the value is <c>None</c>, otherwise an Error result with the specified error value.</returns>
  let inline requireNone (error: 'error) (option: 'value option) : Validation<unit, 'error> =
      match option with
      | Some _ -> Validation.error error
      | None -> Ok()

module CancellableTaskResult =
  open System.Threading.Tasks
  
  let inline error (error: 'error): CancellableTaskResult<'item, 'error> =
    fun _ -> Task.FromResult(Error error)
  
  let inline requireSome (err: 'error) (option: 'ok option): CancellableTaskResult<'ok, 'error> =
    match option with
    | Some x -> CancellableTaskResult.singleton x
    | None -> error err

  let inline mapError
    ([<InlineIfLambda>] mapper: 'errorInput -> 'errorOutput)
    (input: CancellableTaskResult<'ok, 'errorInput>)
    : CancellableTaskResult<'ok, 'errorOutput> =
    CancellableTask.map (Result.mapError mapper) input
    
  let inline foldResult
    ([<InlineIfLambda>] onOk: 'okInput -> 'output)
    ([<InlineIfLambda>] onError: 'errorInput -> 'output)
    (input: CancellableTaskResult<'okInput, 'errorInput>)
    : CancellableTask<'output> =
    CancellableTask.map (Result.either onOk onError) input
    
  let inline ignore<'ok, 'error>
    (input: CancellableTaskResult<'ok, 'error>)
    : CancellableTaskResult<unit, 'error> =
    CancellableTask.map Result.ignore input
    
module TaskSeq =
  open FSharp.Control
  
  open System.Threading.Tasks
  
  let inline traverseValidationA
    ([<InlineIfLambda>] f: 'a -> Validation<'b, 'error>)
    (xs: TaskSeq<'a>)
    : Task<Validation<'b list, 'error>> =
    task {
      let mutable state = Ok []
      
      for x in xs do
        let fR = f x
        
        state <-
          match state, fR with
          | Ok ys, Ok y -> Ok(y :: ys)
          | Error errs1, Error errs2 -> Error(errs2 @ errs1)
          | Ok _, Error errs -> Error errs
          | Error errs, Ok _ -> Error errs
      
      return state |> Result.eitherMap List.rev List.rev
    }

  let inline sequenceValidationA (xs: TaskSeq<Validation<'a, 'error>>)
    : Task<Validation<'a list, 'error>> =
    traverseValidationA id xs
