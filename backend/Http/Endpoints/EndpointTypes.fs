namespace TwoPoint.Http.Endpoints

type ApiResponse<'data> =
  { Message : string option
    Data : 'data option }  

