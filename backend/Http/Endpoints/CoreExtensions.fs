namespace TwoPoint.Core.Util

open System.Net


module QueryResult =
  open TwoPoint.Http.Endpoints
  
  open FsToolkit.ErrorHandling
  
  let toApiResponse (onSuccess: 'input -> 'output) (queryResult : QueryResult<'input>) =
    queryResult
    |>
      Result.either
        (fun success -> { Message = None; Data = Some (onSuccess success) }, HttpStatusCode.OK)
        (fun err ->
          match err with
          | Validation _ ->
            { Message = Some (err.ToString()); Data = None }, HttpStatusCode.BadRequest
          | Dependency _ ->
            { Message = Some (err.ToString()); Data = None }, HttpStatusCode.InternalServerError
        )
