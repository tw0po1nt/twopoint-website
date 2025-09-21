namespace TwoPoint.Http

open System

type AzureConfig =
  { EntraTenantId : Guid
    ManagedIdentityClientId : Guid
    TableStorageUri : Uri }

type Config =
  { Azure : AzureConfig }
  
module Config =
  
  open TwoPoint.Http.Extensions
    
  open Symbolica.Extensions.Configuration.FSharp
  
  let bind config =
    bind {
      let! azure = Bind.section "Azure" <| bind {
        let! entraTenantId = Bind.valueAt "EntraTenantId" Bind.guid
        let! managedIdentityClientId = Bind.valueAt "ManagedIdentityClientId" Bind.guid
        let! tableStorageUri = Bind.valueAt "TableStorageUri" (Bind.uri UriKind.Absolute)
        return
          { EntraTenantId = entraTenantId
            ManagedIdentityClientId = managedIdentityClientId
            TableStorageUri = tableStorageUri }
      }
      
      return { Azure = azure }
    }
    |> Binder.eval config
