# Email Newsletter Setup for Blog Posts

This document describes the email newsletter feature that renders blog posts as email-friendly HTML and uploads them to Azure Blob Storage.

## Overview

The system automatically generates email-optimized HTML versions of all blog posts during production builds. These HTML files use inline CSS for maximum compatibility with email clients and are uploaded to Azure Blob Storage for use in email campaigns.

## Features

- **Email-friendly HTML**: All blog posts are rendered with inline CSS styles
- **Email client compatibility**: Tested formatting that works across major email clients
- **Automated uploads**: Production builds automatically upload to Azure Blob Storage
- **Secure authentication**: Uses Azure DefaultAzureCredential for secure, passwordless authentication
- **Same URL structure**: Email HTML is uploaded to match the website URL structure

## URL Structure

Blog posts follow the same URL pattern in Azure Blob Storage as they do on the website:

- Website: `https://staging.twopoint.dev/posts/danger-lurks-in-system-linq-enumerable-or-default-functions`
- Email HTML: `https://{storage-account}.blob.core.windows.net/{container}/posts/danger-lurks-in-system-linq-enumerable-or-default-functions`

## Setup Instructions

### 1. Azure Resources Setup

Create the required Azure resources:

```bash
# Create resource group (if needed)
az group create --name twopoint-resources --location eastus

# Create storage account
az storage account create \
  --name twopointstorage \
  --resource-group twopoint-resources \
  --location eastus \
  --sku Standard_LRS

# The container will be created automatically by the upload script
```

### 2. Authentication Setup

The upload script uses `DefaultAzureCredential` which supports multiple authentication methods:

#### For Local Development:

Use Azure CLI authentication:
```bash
az login
```

#### For GitHub Actions (Recommended):

Use a Service Principal with federated credentials (passwordless):

1. Create a Service Principal:
```bash
az ad sp create-for-rbac --name "github-actions-twopoint" --sdk-auth
```

2. Assign the "Storage Blob Data Contributor" role:
```bash
az role assignment create \
  --assignee <service-principal-id> \
  --role "Storage Blob Data Contributor" \
  --scope "/subscriptions/<subscription-id>/resourceGroups/twopoint-resources/providers/Microsoft.Storage/storageAccounts/twopointstorage"
```

3. Set up GitHub environment variables (see below)

### 3. Environment Variables

Add these environment variables to your GitHub repository secrets:

| Variable Name | Description | Example Value | Required |
|--------------|-------------|---------------|----------|
| `AZURE_STORAGE_ACCOUNT_NAME` | Name of your Azure Storage account | `twopointstorage` | Yes |
| `AZURE_STORAGE_CONTAINER_NAME` | Container name for email HTML files | `email-posts` | No (defaults to `email-posts`) |
| `AZURE_CLIENT_ID` | Service Principal Application ID | `12345678-1234-...` | Yes (for CI/CD) |
| `AZURE_TENANT_ID` | Azure AD Tenant ID | `87654321-4321-...` | Yes (for CI/CD) |
| `AZURE_CLIENT_SECRET` | Service Principal Secret | `your-secret-value` | Yes (for CI/CD) |

### To add secrets in GitHub:

1. Go to your repository on GitHub
2. Navigate to Settings → Secrets and variables → Actions
3. Click "New repository secret"
4. Add each variable listed above

### 4. Update GitHub Actions Workflow

The production build workflow has been updated to automatically upload email HTML after building. See `.github/workflows/astro-prod.yml` for the implementation.

## Usage

### Local Development

Test email rendering locally:

```bash
# Build the site (includes email HTML generation)
cd frontend
npm run build

# Check generated email HTML
ls dist/email/

# Test upload (requires Azure CLI authentication)
npm run upload:email
```

### Production Deployment

Email HTML is automatically uploaded during production builds when deploying to the main branch. The GitHub Actions workflow:

1. Builds the Astro site (generates email HTML)
2. Runs the upload script to push email HTML to Azure
3. Deploys the main site to GitHub Pages

### Manual Upload

To manually trigger an upload:

```bash
cd frontend
npm run upload:email
```

## Email HTML Structure

Each blog post is rendered using the `EmailBlogLayout.astro` layout which:

- Uses inline CSS for styling (no external stylesheets)
- Employs table-based layout for email client compatibility
- Includes all post metadata (title, author, date, category, tags)
- Applies responsive styling that works on mobile and desktop email clients
- Provides a link back to the web version

## Styling Approach

The email renderer applies inline styles to all HTML elements:

- **Headings**: Bold, appropriately sized with margins
- **Paragraphs**: Proper line height and spacing
- **Links**: Blue color with underline
- **Code blocks**: Monospace font with background color
- **Lists**: Proper indentation and spacing
- **Tables**: Full borders and cell padding
- **Images**: Responsive with max-width

## Security Notes

- **No connection strings**: Never use connection strings; use DefaultAzureCredential instead
- **Least privilege**: Service Principal only has "Storage Blob Data Contributor" role
- **Secrets management**: All credentials stored in GitHub Secrets, never in code
- **Container access**: Container allows public read access to blobs (required for email clients)

## Troubleshooting

### Upload fails with authentication error

Ensure you've set all required environment variables and the Service Principal has the correct role assignment.

### No HTML files found

The build must complete successfully before uploading. Check that `npm run build` creates files in `dist/email/`.

### Container not found

The upload script automatically creates the container if it doesn't exist. Ensure the Service Principal has permission to create containers.

## Email Client Testing

Before sending emails to subscribers, test the HTML in various email clients:

- Gmail (web, mobile)
- Outlook (desktop, web, mobile)
- Apple Mail (iOS, macOS)
- Yahoo Mail
- ProtonMail

Use services like Litmus or Email on Acid for comprehensive testing.

## Future Enhancements

Potential improvements to consider:

- Add email preview functionality to the website
- Implement newsletter subscription management
- Create email templates with different layouts
- Add automated email sending integration
- Track email open rates and engagement
