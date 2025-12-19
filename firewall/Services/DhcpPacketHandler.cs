using NetworkFirewall.Models;

namespace NetworkFirewall.Services;

/// <summary>
/// Parser et builder de paquets DHCP selon RFC 2131
/// </summary>
public static class DhcpPacketHandler
{
    private const int MinPacketSize = 240; // Taille minimale d'un paquet DHCP
    
    // Options DHCP supplémentaires
    private const byte OptionNtpServers = 42;
    private const byte OptionTftpServerName = 66;
    private const byte OptionBootFileName = 67;
    
    /// <summary>
    /// Parser un paquet DHCP brut
    /// </summary>
    public static DhcpPacket? Parse(byte[] data)
    {
        if (data == null || data.Length < MinPacketSize)
            return null;
        
        try
        {
            var packet = new DhcpPacket
            {
                Op = data[0],
                HType = data[1],
                HLen = data[2],
                Hops = data[3],
                Xid = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]),
                Secs = (ushort)((data[8] << 8) | data[9]),
                Flags = (ushort)((data[10] << 8) | data[11]),
                CiAddr = (uint)((data[12] << 24) | (data[13] << 16) | (data[14] << 8) | data[15]),
                YiAddr = (uint)((data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19]),
                SiAddr = (uint)((data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23]),
                GiAddr = (uint)((data[24] << 24) | (data[25] << 16) | (data[26] << 8) | data[27])
            };
            
            // Client hardware address (16 bytes at offset 28)
            Array.Copy(data, 28, packet.ChAddr, 0, 16);
            
            // Server name (64 bytes at offset 44)
            Array.Copy(data, 44, packet.SName, 0, 64);
            
            // Boot file (128 bytes at offset 108)
            Array.Copy(data, 108, packet.File, 0, 128);
            
            // Vérifier le magic cookie DHCP (offset 236)
            if (data.Length < 240 ||
                data[236] != 99 || data[237] != 130 || data[238] != 83 || data[239] != 99)
            {
                return null; // Pas un paquet DHCP valide
            }
            
            // Parser les options DHCP (après le magic cookie)
            int offset = 240;
            while (offset < data.Length)
            {
                byte optionCode = data[offset++];
                
                // Option End
                if (optionCode == (byte)DhcpOption.End)
                    break;
                
                // Option Pad (padding)
                if (optionCode == (byte)DhcpOption.Pad)
                    continue;
                
                // Lire la longueur de l'option
                if (offset >= data.Length)
                    break;
                
                byte optionLength = data[offset++];
                
                if (offset + optionLength > data.Length)
                    break;
                
                // Lire la valeur de l'option
                byte[] optionValue = new byte[optionLength];
                Array.Copy(data, offset, optionValue, 0, optionLength);
                offset += optionLength;
                
                packet.Options[(DhcpOption)optionCode] = optionValue;
            }
            
            return packet;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Construire un paquet DHCP de réponse
    /// </summary>
    public static byte[] Build(DhcpPacket packet)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        
        // Champs fixes (236 bytes)
        writer.Write(packet.Op);
        writer.Write(packet.HType);
        writer.Write(packet.HLen);
        writer.Write(packet.Hops);
        
        // XID en big-endian
        writer.Write((byte)((packet.Xid >> 24) & 0xFF));
        writer.Write((byte)((packet.Xid >> 16) & 0xFF));
        writer.Write((byte)((packet.Xid >> 8) & 0xFF));
        writer.Write((byte)(packet.Xid & 0xFF));
        
        // Secs en big-endian
        writer.Write((byte)((packet.Secs >> 8) & 0xFF));
        writer.Write((byte)(packet.Secs & 0xFF));
        
        // Flags en big-endian
        writer.Write((byte)((packet.Flags >> 8) & 0xFF));
        writer.Write((byte)(packet.Flags & 0xFF));
        
        // Adresses IP en big-endian
        WriteIp(writer, packet.CiAddr);
        WriteIp(writer, packet.YiAddr);
        WriteIp(writer, packet.SiAddr);
        WriteIp(writer, packet.GiAddr);
        
        // Client hardware address (16 bytes)
        writer.Write(packet.ChAddr);
        
        // Server name (64 bytes)
        writer.Write(packet.SName);
        
        // Boot file (128 bytes)
        writer.Write(packet.File);
        
        // Magic cookie DHCP
        writer.Write(DhcpPacket.MagicCookie);
        
        // Options DHCP
        foreach (var option in packet.Options)
        {
            writer.Write((byte)option.Key);
            writer.Write((byte)option.Value.Length);
            writer.Write(option.Value);
        }
        
        // Option End
        writer.Write((byte)DhcpOption.End);
        
        // Padding pour atteindre la taille minimale (300 bytes recommandé)
        while (ms.Length < 300)
            writer.Write((byte)0);
        
        return ms.ToArray();
    }
    
    private static void WriteIp(BinaryWriter writer, uint ip)
    {
        writer.Write((byte)((ip >> 24) & 0xFF));
        writer.Write((byte)((ip >> 16) & 0xFF));
        writer.Write((byte)((ip >> 8) & 0xFF));
        writer.Write((byte)(ip & 0xFF));
    }
    
    /// <summary>
    /// Créer un paquet OFFER en réponse à un DISCOVER
    /// </summary>
    public static DhcpPacket CreateOffer(
        DhcpPacket request,
        uint offeredIp,
        uint serverIp,
        DhcpConfig config)
    {
        var response = CreateBaseResponse(request, offeredIp, serverIp, config);
        
        // Message Type = OFFER
        response.Options[DhcpOption.MessageType] = new byte[] { (byte)DhcpMessageType.Offer };
        
        // Server Identifier
        response.Options[DhcpOption.ServerIdentifier] = DhcpPacket.IpToBytes(serverIp);
        
        AddLeaseTimeOptions(response, config);
        AddNetworkOptions(response, config);
        AddAdvancedOptions(response, config);
        
        return response;
    }
    
    /// <summary>
    /// Créer un paquet ACK en réponse à un REQUEST
    /// </summary>
    public static DhcpPacket CreateAck(
        DhcpPacket request,
        uint assignedIp,
        uint serverIp,
        DhcpConfig config)
    {
        var response = CreateBaseResponse(request, assignedIp, serverIp, config);
        
        // Message Type = ACK
        response.Options[DhcpOption.MessageType] = new byte[] { (byte)DhcpMessageType.Ack };
        
        // Server Identifier
        response.Options[DhcpOption.ServerIdentifier] = DhcpPacket.IpToBytes(serverIp);
        
        AddLeaseTimeOptions(response, config);
        AddNetworkOptions(response, config);
        AddAdvancedOptions(response, config);
        
        return response;
    }
    
    /// <summary>
    /// Créer un paquet NAK (refus)
    /// </summary>
    public static DhcpPacket CreateNak(DhcpPacket request, uint serverIp)
    {
        var response = new DhcpPacket
        {
            Op = 2, // BOOTREPLY
            HType = request.HType,
            HLen = request.HLen,
            Hops = 0,
            Xid = request.Xid,
            Secs = 0,
            Flags = request.Flags,
            CiAddr = 0,
            YiAddr = 0,
            SiAddr = serverIp,
            GiAddr = request.GiAddr
        };
        
        Array.Copy(request.ChAddr, response.ChAddr, request.ChAddr.Length);
        
        response.Options[DhcpOption.MessageType] = new byte[] { (byte)DhcpMessageType.Nak };
        response.Options[DhcpOption.ServerIdentifier] = DhcpPacket.IpToBytes(serverIp);
        
        return response;
    }
    
    private static DhcpPacket CreateBaseResponse(DhcpPacket request, uint assignedIp, uint serverIp, DhcpConfig config)
    {
        var response = new DhcpPacket
        {
            Op = 2, // BOOTREPLY
            HType = request.HType,
            HLen = request.HLen,
            Hops = 0,
            Xid = request.Xid,
            Secs = 0,
            Flags = request.Flags,
            CiAddr = 0,
            YiAddr = assignedIp,
            SiAddr = serverIp,
            GiAddr = request.GiAddr
        };
        
        Array.Copy(request.ChAddr, response.ChAddr, request.ChAddr.Length);
        
        // Support PXE boot - Next server IP
        if (!string.IsNullOrEmpty(config.NextServerIp))
        {
            try
            {
                response.SiAddr = DhcpPacket.StringToIp(config.NextServerIp);
            }
            catch { }
        }
        
        // Boot file name dans le champ file (pour PXE)
        if (!string.IsNullOrEmpty(config.BootFileName))
        {
            var bootFileBytes = System.Text.Encoding.ASCII.GetBytes(config.BootFileName);
            Array.Copy(bootFileBytes, response.File, Math.Min(bootFileBytes.Length, 128));
        }
        
        // Server name (pour TFTP)
        if (!string.IsNullOrEmpty(config.TftpServerName))
        {
            var serverNameBytes = System.Text.Encoding.ASCII.GetBytes(config.TftpServerName);
            Array.Copy(serverNameBytes, response.SName, Math.Min(serverNameBytes.Length, 64));
        }
        
        return response;
    }
    
    private static void AddLeaseTimeOptions(DhcpPacket response, DhcpConfig config)
    {
        // Lease Time
        var leaseSeconds = (uint)(config.LeaseTimeMinutes * 60);
        response.Options[DhcpOption.LeaseTime] = BitConverter.GetBytes(leaseSeconds).Reverse().ToArray();
        
        // Renewal Time (T1)
        var renewalSeconds = config.RenewalTimeMinutes > 0 
            ? (uint)(config.RenewalTimeMinutes * 60) 
            : leaseSeconds / 2;
        response.Options[DhcpOption.RenewalTime] = BitConverter.GetBytes(renewalSeconds).Reverse().ToArray();
        
        // Rebinding Time (T2)
        var rebindingSeconds = config.RebindingTimeMinutes > 0
            ? (uint)(config.RebindingTimeMinutes * 60)
            : (uint)(leaseSeconds * 0.875);
        response.Options[DhcpOption.RebindingTime] = BitConverter.GetBytes(rebindingSeconds).Reverse().ToArray();
    }
    
    private static void AddNetworkOptions(DhcpPacket response, DhcpConfig config)
    {
        // Subnet Mask
        response.Options[DhcpOption.SubnetMask] = DhcpPacket.IpToBytes(DhcpPacket.StringToIp(config.SubnetMask));
        
        // Router (Gateway)
        if (!string.IsNullOrEmpty(config.Gateway))
            response.Options[DhcpOption.Router] = DhcpPacket.IpToBytes(DhcpPacket.StringToIp(config.Gateway));
        
        // DNS Servers
        AddDnsServers(response, config);
        
        // Domain Name
        if (!string.IsNullOrEmpty(config.DomainName))
            response.Options[DhcpOption.DomainName] = System.Text.Encoding.ASCII.GetBytes(config.DomainName);
        
        // Broadcast Address
        if (!string.IsNullOrEmpty(config.BroadcastAddress))
        {
            try
            {
                response.Options[DhcpOption.BroadcastAddress] = DhcpPacket.IpToBytes(DhcpPacket.StringToIp(config.BroadcastAddress));
            }
            catch { }
        }
    }
    
    private static void AddAdvancedOptions(DhcpPacket response, DhcpConfig config)
    {
        // NTP Servers (Option 42)
        var ntpServers = new List<byte>();
        if (!string.IsNullOrEmpty(config.NtpServer1))
        {
            try
            {
                ntpServers.AddRange(DhcpPacket.IpToBytes(DhcpPacket.StringToIp(config.NtpServer1)));
            }
            catch { }
        }
        if (!string.IsNullOrEmpty(config.NtpServer2))
        {
            try
            {
                ntpServers.AddRange(DhcpPacket.IpToBytes(DhcpPacket.StringToIp(config.NtpServer2)));
            }
            catch { }
        }
        if (ntpServers.Count > 0)
            response.Options[(DhcpOption)OptionNtpServers] = ntpServers.ToArray();
        
        // TFTP Server Name (Option 66) - pour PXE boot
        if (!string.IsNullOrEmpty(config.TftpServerName))
            response.Options[(DhcpOption)OptionTftpServerName] = System.Text.Encoding.ASCII.GetBytes(config.TftpServerName);
        
        // Boot File Name (Option 67) - pour PXE boot
        if (!string.IsNullOrEmpty(config.BootFileName))
            response.Options[(DhcpOption)OptionBootFileName] = System.Text.Encoding.ASCII.GetBytes(config.BootFileName);
        
        // Options personnalisées
        if (config.CustomOptions != null)
        {
            foreach (var customOption in config.CustomOptions)
            {
                if (customOption.OptionCode >= 1 && customOption.OptionCode <= 254 && 
                    !string.IsNullOrEmpty(customOption.Value))
                {
                    try
                    {
                        // Essayer de parser comme IP si ça ressemble à une IP
                        if (customOption.Value.Contains('.') && customOption.Value.Split('.').Length == 4)
                        {
                            response.Options[(DhcpOption)customOption.OptionCode] = 
                                DhcpPacket.IpToBytes(DhcpPacket.StringToIp(customOption.Value));
                        }
                        else
                        {
                            // Sinon, encoder en ASCII
                            response.Options[(DhcpOption)customOption.OptionCode] = 
                                System.Text.Encoding.ASCII.GetBytes(customOption.Value);
                        }
                    }
                    catch { }
                }
            }
        }
    }
    
    private static void AddDnsServers(DhcpPacket response, DhcpConfig config)
    {
        var dnsServers = new List<byte>();
        
        if (!string.IsNullOrEmpty(config.Dns1))
        {
            try
            {
                dnsServers.AddRange(DhcpPacket.IpToBytes(DhcpPacket.StringToIp(config.Dns1)));
            }
            catch { }
        }
        
        if (!string.IsNullOrEmpty(config.Dns2))
        {
            try
            {
                dnsServers.AddRange(DhcpPacket.IpToBytes(DhcpPacket.StringToIp(config.Dns2)));
            }
            catch { }
        }
        
        if (!string.IsNullOrEmpty(config.Dns3))
        {
            try
            {
                dnsServers.AddRange(DhcpPacket.IpToBytes(DhcpPacket.StringToIp(config.Dns3)));
            }
            catch { }
        }
        
        if (dnsServers.Count > 0)
            response.Options[DhcpOption.DnsServer] = dnsServers.ToArray();
    }
}
