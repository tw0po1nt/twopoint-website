namespace FsToolkit.ErrorHandling

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
