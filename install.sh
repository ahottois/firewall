#!/bin/bash

# NetGuard Installation Script
# Usage: curl -sSL https://raw.githubusercontent.com/ahottois/firewall/master/install.sh | sudo bash

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

INSTALL_PATH="/opt/netguard"
SERVICE_NAME="netguard"
REPO_URL="https://github.com/ahottois/firewall.git"
TEMP_DIR="/tmp/netguard-install-$$"

echo -e "${BLUE}"
echo "====================================================="
echo "    NetGuard - Network Firewall Monitor Installer"
echo "====================================================="
echo -e "${NC}"

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    echo -e "${RED}Error: Please run as root (sudo)${NC}"
    exit 1
fi

# Check for required tools
echo -e "${YELLOW}[1/6] Checking requirements...${NC}"

if ! command -v git &> /dev/null; then
    echo -e "${RED}Error: git is not installed. Install it with: apt install git${NC}"
    exit 1
fi

if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}Error: .NET SDK is not installed.${NC}"
    echo "Install it with:"
    echo "  wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh"
    echo "  chmod +x dotnet-install.sh"
    echo "  ./dotnet-install.sh --channel 8.0"
    exit 1
fi

# Check .NET version
DOTNET_VERSION=$(dotnet --version 2>/dev/null | cut -d. -f1)
if [ "$DOTNET_VERSION" -lt 8 ]; then
    echo -e "${RED}Error: .NET 8 or higher is required. Current version: $(dotnet --version)${NC}"
    exit 1
fi

echo -e "${GREEN}Requirements OK${NC}"

# Stop existing service if running
echo -e "${YELLOW}[2/6] Stopping existing service...${NC}"
systemctl stop $SERVICE_NAME 2>/dev/null || true
systemctl disable $SERVICE_NAME 2>/dev/null || true

# Clone repository
echo -e "${YELLOW}[3/6] Downloading NetGuard...${NC}"
rm -rf "$TEMP_DIR"
git clone --depth 1 "$REPO_URL" "$TEMP_DIR"

# Get commit hash for version tracking
COMMIT_HASH=$(cd "$TEMP_DIR" && git rev-parse HEAD)

# Build
echo -e "${YELLOW}[4/6] Building application...${NC}"
cd "$TEMP_DIR/firewall"
dotnet publish -c Release -o "$INSTALL_PATH" --verbosity quiet

# Save version
echo "$COMMIT_HASH" > "$INSTALL_PATH/.version"

# Create systemd service
echo -e "${YELLOW}[5/6] Installing service...${NC}"
cat > /etc/systemd/system/$SERVICE_NAME.service << EOF
[Unit]
Description=NetGuard Network Firewall Monitor
After=network.target

[Service]
Type=simple
ExecStart=$INSTALL_PATH/firewall
WorkingDirectory=$INSTALL_PATH
Restart=always
RestartSec=10
User=root
Environment=DOTNET_ROOT=/usr/share/dotnet
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
EOF

# Reload systemd and enable service
systemctl daemon-reload
systemctl enable $SERVICE_NAME

# Start service
echo -e "${YELLOW}[6/6] Starting service...${NC}"
systemctl start $SERVICE_NAME

# Cleanup
rm -rf "$TEMP_DIR"

# Get IP address
IP_ADDR=$(hostname -I | awk '{print $1}')

# Wait a moment for service to start
sleep 2

# Check if service is running
if systemctl is-active --quiet $SERVICE_NAME; then
    echo -e "${GREEN}"
    echo "====================================================="
    echo "    NetGuard installed successfully!"
    echo "====================================================="
    echo ""
    echo -e "    Web Interface: ${BLUE}http://$IP_ADDR:9764${GREEN}"
    echo ""
    echo "    Useful commands:"
    echo "      Status:   sudo systemctl status $SERVICE_NAME"
    echo "      Logs:     sudo journalctl -u $SERVICE_NAME -f"
    echo "      Restart:  sudo systemctl restart $SERVICE_NAME"
    echo "      Stop:     sudo systemctl stop $SERVICE_NAME"
    echo ""
    echo "    Version: ${COMMIT_HASH:0:7}"
    echo "====================================================="
    echo -e "${NC}"
else
    echo -e "${RED}"
    echo "Error: Service failed to start"
    echo "Check logs with: sudo journalctl -u $SERVICE_NAME -n 50"
    echo -e "${NC}"
    exit 1
fi
