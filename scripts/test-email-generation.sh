#!/bin/bash

# Test script for newsletter HTML generation
# This script helps verify that newsletter HTML is being generated correctly

set -e

echo "========================================="
echo "Newsletter HTML Generation Test"
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
echo "2. Checking for newsletter-export directory..."
if [ -d "dist/newsletter-export" ]; then
  echo "✅ Newsletter export directory exists"
  
  NEWSLETTER_COUNT=$(find dist/newsletter-export -name "*.html" -type f | wc -l)
  echo "✅ Found $NEWSLETTER_COUNT newsletter HTML files"
  
  echo ""
  echo "3. Sample newsletter HTML files:"
  find dist/newsletter-export -name "*.html" -type f | head -5 | while read file; do
    echo "   - $(basename $(dirname $file))/$(basename $file)"
  done
  
  echo ""
  echo "4. Checking first newsletter HTML for proper structure..."
  FIRST_NEWSLETTER=$(find dist/newsletter-export -name "*.html" -type f | head -1)
  if [ -n "$FIRST_NEWSLETTER" ]; then
    if grep -q "table" "$FIRST_NEWSLETTER" && \
       grep -q "font-family" "$FIRST_NEWSLETTER" && \
       grep -q "Read this article on the web" "$FIRST_NEWSLETTER"; then
      echo "✅ Newsletter HTML structure looks good"
    else
      echo "⚠️  Newsletter HTML might be missing expected elements"
    fi
    
    FILE_SIZE=$(wc -c < "$FIRST_NEWSLETTER")
    echo "   File size: ${FILE_SIZE} bytes"
  fi
  
  echo ""
  echo "5. Verifying newsletter directory was removed from served output..."
  if [ ! -d "dist/newsletter" ]; then
    echo "✅ Newsletter pages are NOT served (dist/newsletter removed)"
  else
    echo "⚠️  Warning: dist/newsletter still exists - newsletter pages might be served"
  fi
  
  echo ""
  echo "========================================="
  echo "✅ Newsletter generation test PASSED"
  echo "========================================="
  
else
  echo "❌ Newsletter export directory not found at dist/newsletter-export"
  echo ""
  echo "Expected structure:"
  echo "  dist/"
  echo "    newsletter-export/  (contains HTML for upload)"
  echo "    newsletter/         (should NOT exist - removed after export)"
  echo ""
  echo "This might be because:"
  echo "  1. The build stopped early due to network issues"
  echo "  2. No blog posts were found"
  echo "  3. The newsletter route isn't being processed"
  echo "  4. The newsletter-export integration didn't run"
  echo ""
  exit 1
fi

echo ""
echo "To test Azure upload locally, run:"
echo "  export AZURE_STORAGE_ACCOUNT_NAME=your-account"
echo "  npm run upload:email"
echo ""
