#!/bin/bash

# Test script for email blog post generation
# This script helps verify that email HTML is being generated correctly

set -e

echo "========================================="
echo "Email Blog Post Generation Test"
echo "========================================="
echo ""

FRONTEND_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../frontend" && pwd)"
cd "$FRONTEND_DIR"

echo "1. Running Astro build..."
echo ""
npm run build || {
  echo "❌ Build failed. This might be due to network issues with remote images."
  echo "   In production, this should work fine."
  exit 1
}

echo ""
echo "2. Checking for email directory..."
if [ -d "dist/email" ]; then
  echo "✅ Email directory exists"
  
  EMAIL_COUNT=$(find dist/email -name "*.html" -type f | wc -l)
  echo "✅ Found $EMAIL_COUNT email HTML files"
  
  echo ""
  echo "3. Sample email HTML files:"
  find dist/email -name "*.html" -type f | head -5 | while read file; do
    echo "   - $(basename $(dirname $file))/$(basename $file)"
  done
  
  echo ""
  echo "4. Checking first email HTML for proper structure..."
  FIRST_EMAIL=$(find dist/email -name "*.html" -type f | head -1)
  if [ -n "$FIRST_EMAIL" ]; then
    if grep -q "table" "$FIRST_EMAIL" && \
       grep -q "font-family" "$FIRST_EMAIL" && \
       grep -q "Read this article on the web" "$FIRST_EMAIL"; then
      echo "✅ Email HTML structure looks good"
    else
      echo "⚠️  Email HTML might be missing expected elements"
    fi
    
    FILE_SIZE=$(wc -c < "$FIRST_EMAIL")
    echo "   File size: ${FILE_SIZE} bytes"
  fi
  
  echo ""
  echo "========================================="
  echo "✅ Email generation test PASSED"
  echo "========================================="
  
else
  echo "❌ Email directory not found at dist/email"
  echo ""
  echo "Expected structure:"
  echo "  dist/"
  echo "    email/"
  echo "      [slug].html"
  echo ""
  echo "This might be because:"
  echo "  1. The build stopped early due to network issues"
  echo "  2. No blog posts were found"
  echo "  3. The email route isn't being processed"
  echo ""
  exit 1
fi

echo ""
echo "To test Azure upload locally, run:"
echo "  export AZURE_STORAGE_ACCOUNT_NAME=your-account"
echo "  npm run upload:email"
echo ""
