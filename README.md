# NetGuard - Network Firewall Monitor

A real-time network monitoring and security tool for home networks.

## Features

- **Device Discovery & Tracking** - Automatically detect all devices on your network
- **Real-time Packet Analysis** - Monitor network traffic in real-time
- **Anomaly Detection** - Detect port scans, ARP spoofing, and suspicious traffic
- **Camera Detection** - Find IP cameras and check for default passwords
- **Live Notifications** - Get instant alerts for security events
- **Traffic Logging** - Log and analyze network traffic patterns

## Quick Install (Ubuntu/Debian)

### Prerequisites

1. Install .NET 8 SDK:
```bash
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
sudo ./dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet
sudo ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
```

2. Install libpcap:
```bash
sudo apt install libpcap-dev git -y
```

### One-Line Install

```bash
curl -sSL https://raw.githubusercontent.com/ahottois/firewall/master/install.sh | sudo bash
```

### Manual Install

```bash
cd ~ && sudo rm -rf firewall && git clone https://github.com/ahottois/firewall.git && cd firewall/firewall && sudo dotnet publish -c Release -o /opt/netguard && sudo bash ../install.sh
```

## Usage

After installation, access the web interface at:
```
http://YOUR-SERVER-IP:9764
```

### Service Commands

```bash
# Check status
sudo systemctl status netguard

# View logs
sudo journalctl -u netguard -f

# Restart
sudo systemctl restart netguard

# Stop
sudo systemctl stop netguard

# Start
sudo systemctl start netguard
```

### Update

From web interface: Go to **Administration** > **Update**

Or via command line:
```bash
curl -sSL https://raw.githubusercontent.com/ahottois/firewall/master/update.sh | sudo bash
```

## Configuration

Edit `/opt/netguard/appsettings.json`:

```json
{
  "AppSettings": {
    "WebPort": 9764,
    "DatabasePath": "/opt/netguard/data/firewall.db",
    "NetworkInterface": "",
    "EnablePacketCapture": true,
    "PortScanThreshold": 20,
    "PortScanTimeWindowSeconds": 60,
    "AlertRetentionDays": 30,
    "TrafficLogRetentionDays": 7,
    "SuspiciousPorts": [22, 23, 135, 139, 445, 3389]
  }
}
```

## Requirements

- Ubuntu 20.04+ or Debian 11+
- .NET 8 Runtime/SDK
- libpcap
- Root access (for packet capture)

## License

MIT License