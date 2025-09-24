namespace TwoPoint.Http.Endpoints

type ApiResponse<'data> =
  { Success : bool
    Message : string option
    Data : 'data option }  

