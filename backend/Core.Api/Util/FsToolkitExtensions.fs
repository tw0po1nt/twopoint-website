namespace FsToolkit.ErrorHandling

// todo: open PR in FsToolkit for all these

module Option =
  open IcedTasks
  
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

  let inline traverseCancellableTaskResult
    ([<InlineIfLambda>] f: 'a -> CancellableTaskResult<'b, 'error>)
    (res: Option<'a>)
    : CancellableTaskResult<'b option, 'error> =
    sequenceCancellableTaskResult (Option.map f res)

module Result =
  open IcedTasks
  
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

  let inline traverseCancellableTask
    ([<InlineIfLambda>] f: 'a -> CancellableTask<'b>)
    (res: Result<'a, 'c>)
    : CancellableTaskResult<'b, 'c> =
    sequenceCancellableTask (Result.map f res)
    
  let inline bitraverseCancellableTask
    ([<InlineIfLambda>] f: 'a -> CancellableTask<'b>)
    ([<InlineIfLambda>] g: 'c -> CancellableTask<'d>)
    (res: Result<'a, 'c>)
    : CancellableTaskResult<'b, 'd> =
      cancellableTask {
        match res with
        | Ok a ->
          let! b = f a
          return Ok b
        | Error c ->
          let! d = g c
          return Error d
      }

module CancellableTaskResult =
  open IcedTasks
  
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
    
