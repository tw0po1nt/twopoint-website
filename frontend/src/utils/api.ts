export function getApiUri(stage: string) {
    switch (stage) {
        case "local":
            return "http://localhost:7265"
        case "staging":
        case "production":
            return "https://func-twopoint-website-api.azurewebsites.net"
        default:
    }
}