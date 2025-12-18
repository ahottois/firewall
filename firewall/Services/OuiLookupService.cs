using System.Collections.Frozen;

namespace NetworkFirewall.Services;

public interface IOuiLookupService
{
    string? GetVendor(string macAddress);
}

public class OuiLookupService : IOuiLookupService
{
    // Dictionnaire OUI des fabricants les plus courants (préfixe MAC -> Fabricant)
    // ATTENTION: Pas de clés dupliquées!
    private static readonly FrozenDictionary<string, string> _ouiDatabase = new Dictionary<string, string>
    {
        // Apple
        { "000A27", "Apple" }, { "000A95", "Apple" }, { "000D93", "Apple" },
        { "0010FA", "Apple" }, { "001124", "Apple" }, { "001451", "Apple" },
        { "0016CB", "Apple" }, { "0017F2", "Apple" }, { "0019E3", "Apple" },
        { "001B63", "Apple" }, { "001CB3", "Apple" }, { "001D4F", "Apple" },
        { "001E52", "Apple" }, { "001EC2", "Apple" }, { "001F5B", "Apple" },
        { "001FF3", "Apple" }, { "002312", "Apple" }, { "002332", "Apple" },
        { "002436", "Apple" }, { "00254B", "Apple" }, { "0025BC", "Apple" },
        { "002608", "Apple" }, { "00264A", "Apple" }, { "0026B0", "Apple" },
        { "0026BB", "Apple" }, { "003065", "Apple" }, { "003EE1", "Apple" },
        { "0050E4", "Apple" }, { "006171", "Apple" }, { "00A040", "Apple" },
        { "00B362", "Apple" }, { "00C610", "Apple" }, { "00CDFE", "Apple" },
        { "00F4B9", "Apple" }, { "00F76F", "Apple" }, { "041E64", "Apple" },
        { "045453", "Apple" }, { "04D3CF", "Apple" }, { "04E536", "Apple" },
        { "04F7E4", "Apple" }, { "086698", "Apple" }, { "0C74C2", "Apple" },
        { "0CD746", "Apple" }, { "10DDB1", "Apple" }, { "18AF8F", "Apple" },
        { "1C1AC0", "Apple" }, { "2C200B", "Apple" }, { "34C059", "Apple" },
        { "3C0754", "Apple" }, { "40A6D9", "Apple" }, { "44D884", "Apple" },
        { "4C8D79", "Apple" }, { "503237", "Apple" }, { "5855CA", "Apple" },
        { "5C5948", "Apple" }, { "5CF5DA", "Apple" }, { "609217", "Apple" },
        { "64A3CB", "Apple" }, { "68644B", "Apple" }, { "6C4008", "Apple" },
        { "70DEE2", "Apple" }, { "74E2F5", "Apple" }, { "7831C1", "Apple" },
        { "7C6DF8", "Apple" }, { "80006E", "Apple" }, { "80E650", "Apple" },
        { "8C8590", "Apple" }, { "90840D", "Apple" }, { "9803D8", "Apple" },
        { "9C207B", "Apple" }, { "A45E60", "Apple" }, { "A4B197", "Apple" },
        { "A4D1D2", "Apple" }, { "A82066", "Apple" }, { "A85B78", "Apple" },
        { "A860B6", "Apple" }, { "AC293A", "Apple" }, { "B4F0AB", "Apple" },
        { "B8C75D", "Apple" }, { "BC3BAF", "Apple" }, { "C42C03", "Apple" },
        { "C82A14", "Apple" }, { "CC08E0", "Apple" }, { "D023DB", "Apple" },
        { "D0E140", "Apple" }, { "D4619D", "Apple" }, { "D89E3F", "Apple" },
        { "DC2B2A", "Apple" }, { "E0B9BA", "Apple" }, { "E4CE8F", "Apple" },
        { "E8040B", "Apple" }, { "F0B479", "Apple" }, { "F0DCE2", "Apple" },
        
        // Samsung
        { "00124E", "Samsung" }, { "0015B9", "Samsung" }, { "001632", "Samsung" },
        { "001A8A", "Samsung" }, { "001EE1", "Samsung" }, { "001EE2", "Samsung" },
        { "002339", "Samsung" }, { "0024E9", "Samsung" },
        { "08D42B", "Samsung" }, { "10D38A", "Samsung" },
        { "14568E", "Samsung" }, { "14B484", "Samsung" }, { "18227E", "Samsung" },
        { "1C62B8", "Samsung" }, { "206E9C", "Samsung" }, { "24DB96", "Samsung" },
        { "283737", "Samsung" }, { "2C4401", "Samsung" }, { "30CBF8", "Samsung" },
        { "34C3AC", "Samsung" }, { "38D40B", "Samsung" }, { "40B89A", "Samsung" },
        { "44F459", "Samsung" }, { "4844F7", "Samsung" }, { "50A4D0", "Samsung" },
        { "54FA3E", "Samsung" }, { "5CE0C5", "Samsung" }, { "6077E2", "Samsung" },
        { "7825AD", "Samsung" }, { "84119E", "Samsung" }, { "8C71F8", "Samsung" },
        { "94D771", "Samsung" }, { "9C02D7", "Samsung" }, { "A0821F", "Samsung" },
        { "A8F274", "Samsung" }, { "B0EC71", "Samsung" }, { "B407F9", "Samsung" },
        { "B8D9CE", "Samsung" }, { "C45006", "Samsung" }, { "D022BE", "Samsung" },
        { "D8578E", "Samsung" }, { "E4B021", "Samsung" }, { "E83A12", "Samsung" },
        { "F008F1", "Samsung" }, { "F0D9B8", "Samsung" }, { "F49F54", "Samsung" },
        
        // Intel
        { "001111", "Intel" }, { "001302", "Intel" }, { "001500", "Intel" },
        { "0016EA", "Intel" }, { "0016EB", "Intel" }, { "00166F", "Intel" },
        { "001E64", "Intel" }, { "001E65", "Intel" }, { "001E67", "Intel" },
        { "001F3B", "Intel" }, { "001F3C", "Intel" }, { "00215C", "Intel" },
        { "00215D", "Intel" }, { "002314", "Intel" }, { "00248C", "Intel" },
        { "0026C6", "Intel" }, { "0026C7", "Intel" }, { "0027EE", "Intel" },
        { "003067", "Intel" }, { "003676", "Intel" }, { "00A0C9", "Intel" },
        { "00AA00", "Intel" }, { "3C970E", "Intel" },
        { "40A6B7", "Intel" }, { "485B39", "Intel" }, { "4CEF5D", "Intel" },
        { "58A839", "Intel" }, { "5C514F", "Intel" }, { "6036DD", "Intel" },
        { "68053B", "Intel" }, { "6891D0", "Intel" }, { "7C7A91", "Intel" },
        { "80861B", "Intel" }, { "848F69", "Intel" }, { "88B4A6", "Intel" },
        { "8C8D28", "Intel" }, { "94659C", "Intel" }, { "989096", "Intel" },
        { "9CEBE8", "Intel" }, { "A0369F", "Intel" }, { "A0A8CD", "Intel" },
        { "A4C494", "Intel" }, { "B4B676", "Intel" }, { "C8D719", "Intel" },
        { "D0ABD5", "Intel" }, { "D4258B", "Intel" }, { "E4A7A0", "Intel" },
        
        // Raspberry Pi
        { "B827EB", "Raspberry Pi" }, { "DCA632", "Raspberry Pi" },
        { "E45F01", "Raspberry Pi" }, { "D83ADD", "Raspberry Pi" },
        
        // Google / Nest
        { "1C7B21", "Google" }, { "94EB2C", "Google" },
        { "F47F35", "Google" }, { "F4F5D8", "Google" }, { "3C5AB4", "Google" },
        
        // Amazon
        { "0012F2", "Amazon" }, { "40B4CD", "Amazon" }, { "44650D", "Amazon" },
        { "6837E9", "Amazon" }, { "74C246", "Amazon" }, { "7C6191", "Amazon" },
        { "84D6D0", "Amazon" }, { "A002DC", "Amazon" }, { "AC63BE", "Amazon" },
        { "B47C9C", "Amazon" }, { "CC9EA2", "Amazon" }, { "F0272D", "Amazon" },
        
        // Cisco
        { "000142", "Cisco" }, { "00024A", "Cisco" }, { "00036B", "Cisco" },
        { "000819", "Cisco" }, { "000A41", "Cisco" }, { "000B45", "Cisco" },
        { "000D28", "Cisco" }, { "000E38", "Cisco" }, { "0012DA", "Cisco" },
        { "00142B", "Cisco" }, { "0016C7", "Cisco" }, { "0018B9", "Cisco" },
        { "001A6C", "Cisco" }, { "001B54", "Cisco" }, { "001D46", "Cisco" },
        { "001E7A", "Cisco" }, { "002155", "Cisco" }, { "002350", "Cisco" },
        
        // Microsoft
        { "001DD8", "Microsoft" }, { "0025AE", "Microsoft" }, { "0050F2", "Microsoft" },
        { "28186D", "Microsoft" }, { "7C1E52", "Microsoft" },
        { "B4AE2B", "Microsoft" }, { "C8D9D2", "Microsoft" }, { "DC536C", "Microsoft" },
        
        // HP
        { "001083", "HP" }, { "0014C2", "HP" }, { "001871", "HP" },
        { "001A4B", "HP" }, { "001E0B", "HP" }, { "0021F4", "HP" },
        { "0022A5", "HP" }, { "002655", "HP" },
        { "00306E", "HP" }, { "0402B3", "HP" }, { "08002B", "HP" },
        
        // Dell
        { "000874", "Dell" }, { "00188B", "Dell" }, { "001A19", "Dell" },
        { "001E4F", "Dell" }, { "0024E8", "Dell" },
        { "002648", "Dell" }, { "00B0D0", "Dell" }, { "18A99B", "Dell" },
        { "246E96", "Dell" }, { "34E6D7", "Dell" }, { "5C260A", "Dell" },
        
        // TP-Link
        { "001732", "TP-Link" }, { "14CC20", "TP-Link" }, { "14CF92", "TP-Link" },
        { "1C3BF3", "TP-Link" }, { "30B49E", "TP-Link" }, { "3CE824", "TP-Link" },
        { "50C7BF", "TP-Link" }, { "54E6FC", "TP-Link" }, { "647002", "TP-Link" },
        { "6C3B6B", "TP-Link" }, { "948854", "TP-Link" }, { "98DAC4", "TP-Link" },
        { "AC15A2", "TP-Link" }, { "B0BE76", "TP-Link" }, { "C025E9", "TP-Link" },
        { "D8077B", "TP-Link" }, { "E894F6", "TP-Link" }, { "F4EC38", "TP-Link" },
        
        // Netgear
        { "000FB5", "Netgear" }, { "00146C", "Netgear" }, { "001B2F", "Netgear" },
        { "001E2A", "Netgear" }, { "001F33", "Netgear" }, { "00223F", "Netgear" },
        { "00224D", "Netgear" }, { "002438", "Netgear" }, { "00264D", "Netgear" },
        { "204E71", "Netgear" }, { "30469A", "Netgear" }, { "E0469A", "Netgear" },
        
        // Asus
        { "000C6E", "Asus" }, { "00112F", "Asus" }, { "001731", "Asus" },
        { "001A92", "Asus" }, { "001E8C", "Asus" }, { "002215", "Asus" },
        { "08606E", "Asus" }, { "107B44", "Asus" }, { "2C56DC", "Asus" },
        { "305A3A", "Asus" }, { "50465D", "Asus" }, { "544822", "Asus" },
        
        // VMware
        { "000569", "VMware" }, { "000C29", "VMware" }, { "001C14", "VMware" },
        { "005056", "VMware" },
        
        // VirtualBox (Oracle)
        { "080027", "VirtualBox" },
        
        // Xiaomi
        { "0C1DAF", "Xiaomi" }, { "286C07", "Xiaomi" }, { "34CE00", "Xiaomi" },
        { "640980", "Xiaomi" }, { "7C1DD9", "Xiaomi" }, { "8CBEBE", "Xiaomi" },
        { "ACF7F3", "Xiaomi" }, { "F0B429", "Xiaomi" }, { "F8A45F", "Xiaomi" },
        
        // Huawei
        { "001E10", "Huawei" }, { "002568", "Huawei" }, { "0034FE", "Huawei" },
        { "04C06F", "Huawei" }, { "083E5D", "Huawei" }, { "0C45BA", "Huawei" },
        { "10C61F", "Huawei" }, { "24DF6A", "Huawei" }, { "28A6DB", "Huawei" },
        { "48AD08", "Huawei" }, { "54A51B", "Huawei" }, { "5C7D5E", "Huawei" },
        
        // Sony
        { "0013A9", "Sony" }, { "001A80", "Sony" }, { "001D0D", "Sony" },
        { "0024BE", "Sony" }, { "002780", "Sony" }, { "04761E", "Sony" },
        { "285A27", "Sony" }, { "40B837", "Sony" }, { "70D4F2", "Sony" },
        
        // LG
        { "001C62", "LG" }, { "001E75", "LG" }, { "001FE3", "LG" },
        { "002483", "LG" }, { "002663", "LG" }, { "10F96F", "LG" },
        { "2021A5", "LG" }, { "2C54CF", "LG" }, { "34FC6F", "LG" },
        
        // Lenovo
        { "00061B", "Lenovo" }, { "001E68", "Lenovo" },
        { "002564", "Lenovo" }, { "28D244", "Lenovo" }, { "4CE933", "Lenovo" },
        { "6C0B84", "Lenovo" }, { "7011BB", "Lenovo" }, { "98FA9B", "Lenovo" },
        
        // Synology
        { "0011320", "Synology" }, { "001132", "Synology" },
        
        // Ubiquiti
        { "0027220", "Ubiquiti" }, { "002722", "Ubiquiti" }, { "04180F", "Ubiquiti" },
        { "24A43C", "Ubiquiti" }, { "687251", "Ubiquiti" }, { "788A20", "Ubiquiti" },
        { "802AA8", "Ubiquiti" }, { "B4FBE4", "Ubiquiti" }, { "F09FC2", "Ubiquiti" },
        
        // Roku
        { "B0A737", "Roku" }, { "C83A6B", "Roku" }, { "D83134", "Roku" },
        
        // Sonos
        { "000E58", "Sonos" }, { "5CDAD4", "Sonos" }, { "7828CA", "Sonos" },
        { "949F3E", "Sonos" }, { "B8E937", "Sonos" },
        
        // Ring (Amazon)
        { "347E5C", "Ring" }, { "D03972", "Ring" },
        
        // Philips Hue
        { "001788", "Philips Hue" }, { "ECB5FA", "Philips Hue" },
        
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public string? GetVendor(string macAddress)
    {
        if (string.IsNullOrEmpty(macAddress)) return null;
        
        // Normaliser l'adresse MAC et extraire l'OUI (6 premiers caractères)
        var normalized = macAddress
            .Replace(":", "")
            .Replace("-", "")
            .Replace(".", "")
            .ToUpperInvariant();
        
        if (normalized.Length < 6) return null;
        
        var oui = normalized[..6];
        return _ouiDatabase.GetValueOrDefault(oui);
    }
}
