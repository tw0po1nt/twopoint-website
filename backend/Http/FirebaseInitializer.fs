namespace TwoPoint.Http

open System
open System.IO
open System.Threading.Tasks
open Azure.Identity
open Azure.Security.KeyVault.Secrets
open FirebaseAdmin
open Google.Apis.Auth.OAuth2

module FirebaseInitializer =
  
  let private createFromFile (path: string) =
    if not (File.Exists(path)) then
      failwithf $"Firebase service account file not found at: %s{path}"

    let credential = GoogleCredential.FromFile(path)
    FirebaseApp.Create(AppOptions(Credential = credential))
  
  let private createFromJson (json: string) =
    let credential = GoogleCredential.FromJson(json)
    FirebaseApp.Create(AppOptions(Credential = credential))
  
  let private getSecretFromKeyVault (keyVaultUri: Uri) (secretName: string) (credential: DefaultAzureCredential) : Task<string> =
    task {
      let client = SecretClient(keyVaultUri, credential)
      let! response = client.GetSecretAsync(secretName)
      return response.Value.Value
    }
  
  /// Initialize Firebase Admin SDK
  /// Priority: 1) File path (for local dev, if file exists), 2) Key Vault secret name (for Azure)
  let initialize (config: Config) (credential: DefaultAzureCredential option) : Task<FirebaseApp> =
    // Check if Firebase is already initialized
    if FirebaseApp.DefaultInstance <> null then
      Task.FromResult(FirebaseApp.DefaultInstance)
    else
      // Determine the initialization strategy before entering the task
      let useFilePath = 
        config.Firebase.ServiceAccountJsonPath 
        |> Option.filter File.Exists
      
      match useFilePath, config.Firebase.ServiceAccountJsonFromKeyVault, config.Azure.KeyVaultUri with
      // Local development: use file path if the file actually exists
      | Some filePath, _, _ ->
          Task.FromResult(createFromFile filePath)
      
      // Azure: use Key Vault (or fall back to Key Vault if file doesn't exist)
      | None, Some secretName, Some keyVaultUri ->
          match credential with
          | Some cred ->
              task {
                let! json = getSecretFromKeyVault keyVaultUri secretName cred
                return createFromJson json
              }
          | None ->
              failwith "Azure credential is required when using Key Vault for Firebase configuration"
      
      // File path configured but file doesn't exist and no Key Vault fallback
      | None, None, _ when config.Firebase.ServiceAccountJsonPath.IsSome ->
          failwithf $"Firebase service account file not found at: %s{config.Firebase.ServiceAccountJsonPath.Value}. For Azure deployment, configure ServiceAccountJsonFromKeyVault and KeyVaultUri instead."

      // Key Vault secret name provided but no Key Vault URI
      | None, Some _, None ->
          failwith "Firebase ServiceAccountJsonFromKeyVault is configured but Azure KeyVaultUri is missing"
      
      // Neither configured
      | None, None, _ ->
          failwith "Firebase configuration is missing. Please configure either ServiceAccountJsonPath (local) or ServiceAccountJsonFromKeyVault with KeyVaultUri (Azure)"
