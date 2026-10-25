namespace TwoPoint.Core.Util

open System.Net


module QueryResult =
  open TwoPoint.Http.Endpoints

  open FsToolkit.ErrorHandling

  let toApiResponse (onSuccess: 'input -> 'output) (queryResult : QueryResult<'input>) =
    queryResult
    |>
      Result.either
        (fun success -> { Success = true; Message = None; Data = Some (onSuccess success) }, HttpStatusCode.OK)
        (fun err ->
          match err with
          | Validation _ ->
            { Success = false; Message = Some (err.ToString()); Data = None }, HttpStatusCode.BadRequest
          | Dependency _ ->
            { Success = false; Message = Some (err.ToString()); Data = None }, HttpStatusCode.InternalServerError
        )

module ActionResult =
  open TwoPoint.Http.Endpoints

  let toApiResponseWithStatus
    (successStatus : HttpStatusCode)
    (onSuccess : 'event -> 'data option)
    (onLogic : 'logicError -> string * HttpStatusCode)
    (result : ActionResult<'event, 'logicError>)
    : ApiResponse<'data> * HttpStatusCode =
    let failure message statusCode = { Success = false; Message = Some message; Data = None }, statusCode

    match result with
    | Ok event -> { Success = true; Message = None; Data = onSuccess event }, successStatus
    | Error(ActionError.Logic logicError) ->
      let message, statusCode = onLogic logicError
      failure message statusCode
    | Error(ActionError.Validation _ as actionError) -> failure (actionError.ToString()) HttpStatusCode.BadRequest
    | Error(ActionError.Dependency _ as actionError) -> failure (actionError.ToString()) HttpStatusCode.InternalServerError

  let toApiResponse onSuccess onLogic result =
    toApiResponseWithStatus HttpStatusCode.OK onSuccess onLogic result
