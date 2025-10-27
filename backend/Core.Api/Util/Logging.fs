namespace TwoPoint.Core.Util

[<AutoOpen>]
module Logging =
  
  open FsToolkit.ErrorHandling
  open Microsoft.Extensions.Logging
  
  let runDependencyWithLogging
    dependency
    (logger: ILogger)
    (op: string)
    (fn: CancellableTaskResult<'a, DependencyError>) =
      cancellableTaskResult {       
        try
          logger.LogInformation("Executing '{op}'", op)
          let! result = fn
          logger.LogInformation("'{op}' completed successfully", op)
          return result
        with ex ->
          logger.LogError(ex, "{op} failed", op)
          let error =
            { Dependency = dependency
              Type = Unknown ex }
          return! Error error
      }
