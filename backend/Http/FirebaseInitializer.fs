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
    task {
      // Check if Firebase is already initialized
      if FirebaseApp.DefaultInstance <> null then
        return FirebaseApp.DefaultInstance
      else
        match config.Firebase.ServiceAccountJsonPath, config.Firebase.ServiceAccountJsonFromKeyVault, config.Azure.KeyVaultUri with
        // Local development: use file path only if the file actually exists
        | Some filePath, _, _ when File.Exists(filePath) ->
            return createFromFile filePath
        
        // Azure: use Key Vault (or fall back to Key Vault if file doesn't exist)
        | _, Some secretName, Some keyVaultUri ->
            match credential with
            | Some cred ->
                let! json = getSecretFromKeyVault keyVaultUri secretName cred
                return createFromJson json
            | None ->
                return failwith "Azure credential is required when using Key Vault for Firebase configuration"
        
        // File path configured but file doesn't exist and no Key Vault fallback
        | Some filePath, None, _ ->
            return failwithf "Firebase service account file not found at: %s. For Azure deployment, configure ServiceAccountJsonFromKeyVault and KeyVaultUri instead." filePath
        
        // Key Vault secret name provided but no Key Vault URI
        | _, Some _, None ->
            return failwith "Firebase ServiceAccountJsonFromKeyVault is configured but Azure KeyVaultUri is missing"
        
        // Neither configured
        | None, None, _ ->
            return failwith "Firebase configuration is missing. Please configure either ServiceAccountJsonPath (local) or ServiceAccountJsonFromKeyVault with KeyVaultUri (Azure)"
    }
