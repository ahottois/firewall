#!/bin/bash

# NetGuard Update Script
# Usage: sudo /opt/netguard/update.sh

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

INSTALL_PATH="/opt/netguard"
SERVICE_NAME="netguard"
REPO_URL="https://github.com/ahottois/firewall.git"
TEMP_DIR="/tmp/netguard-update-$$"

echo -e "${BLUE}"
echo "====================================================="
echo "    NetGuard - Update"
echo "====================================================="
echo -e "${NC}"

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    echo -e "${RED}Error: Please run as root (sudo)${NC}"
    exit 1
fi

# Get current version
CURRENT_VERSION="unknown"
if [ -f "$INSTALL_PATH/.version" ]; then
    CURRENT_VERSION=$(cat "$INSTALL_PATH/.version")
fi
echo -e "Current version: ${YELLOW}${CURRENT_VERSION:0:7}${NC}"

# Clone repository
echo -e "${YELLOW}Downloading latest version...${NC}"
rm -rf "$TEMP_DIR"
git clone --depth 1 "$REPO_URL" "$TEMP_DIR"

# Get new commit hash
NEW_VERSION=$(cd "$TEMP_DIR" && git rev-parse HEAD)
echo -e "Latest version:  ${GREEN}${NEW_VERSION:0:7}${NC}"

if [ "$CURRENT_VERSION" = "$NEW_VERSION" ]; then
    echo -e "${GREEN}Already up to date!${NC}"
    rm -rf "$TEMP_DIR"
    exit 0
fi

# Build
echo -e "${YELLOW}Building...${NC}"
cd "$TEMP_DIR/firewall"
dotnet publish -c Release -o "$INSTALL_PATH" --verbosity quiet

# Save version
echo "$NEW_VERSION" > "$INSTALL_PATH/.version"

# Cleanup
rm -rf "$TEMP_DIR"

# Restart service
echo -e "${YELLOW}Restarting service...${NC}"
systemctl restart $SERVICE_NAME

sleep 2

if systemctl is-active --quiet $SERVICE_NAME; then
    echo -e "${GREEN}"
    echo "====================================================="
    echo "    Update successful!"
    echo "    Version: ${NEW_VERSION:0:7}"
    echo "====================================================="
    echo -e "${NC}"
else
    echo -e "${RED}Error: Service failed to start after update${NC}"
    exit 1
fi
