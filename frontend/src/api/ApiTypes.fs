namespace TwoPoint.Api

type ApiResponse<'T> =
  { Success : bool
    Message : string option
    Data : 'T option }

