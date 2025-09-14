namespace TwoPoint.Http

open System

type AzureConfig =
  { EntraTenantId : Guid
    TableStorageUri : Uri }

type Config =
  { Azure : AzureConfig }
  
module Config =
  
  open Symbolica.Extensions.Configuration.FSharp
  
  let bind config =
    let bindAzure =
      bind {
        let! entraTenantId = 
      }
    
    bind {
      
    }
