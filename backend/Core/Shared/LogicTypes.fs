namespace TwoPoint.Core.Shared

/// <summary>
/// A business decision. The result of a pure business logic calculation (i.e. no side effects)
/// It is a type alias of the <c>Validation</c> type.
/// A lightweight DSL for pure logic
/// </summary>
type Decision<'success, 'failure> = Result<'success, 'failure>

module Decision =
  
  let inline success x : Decision<'success, 'failure> = Ok x
  
  let inline failure x : Decision<'success, 'failure> = Error x
