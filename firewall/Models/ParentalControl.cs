using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NetworkFirewall.Models;

/// <summary>
/// Profil d'un enfant avec ses appareils et restrictions associés
/// </summary>
public class ChildProfile
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Avatar/Photo en base64 ou URL
    /// </summary>
    [MaxLength(500)]
    public string? AvatarUrl { get; set; }
    
    /// <summary>
    /// Couleur du profil pour l'UI (hex)
    /// </summary>
    [MaxLength(7)]
    public string Color { get; set; } = "#00d9ff";
    
    /// <summary>
    /// Temps d'écran maximum par jour en minutes (0 = illimité)
    /// </summary>
    public int DailyTimeLimitMinutes { get; set; } = 0;
    
    /// <summary>
    /// Temps utilisé aujourd'hui en minutes
    /// </summary>
    [NotMapped]
    public int UsedTimeToday { get; set; } = 0;
    
    /// <summary>
    /// Profil actif ou désactivé
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Pause instantanée activée (coupe tout immédiatement)
    /// </summary>
    public bool IsPaused { get; set; } = false;
    
    /// <summary>
    /// Message affiché quand l'accès est bloqué
    /// </summary>
    [MaxLength(500)]
    public string BlockedMessage { get; set; } = "L'accès Internet est temporairement désactivé.";
    
    /// <summary>
    /// Date de création
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Dernière mise à jour
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public virtual ICollection<ProfileDevice> Devices { get; set; } = new List<ProfileDevice>();
    public virtual ICollection<TimeSchedule> Schedules { get; set; } = new List<TimeSchedule>();
    public virtual ICollection<WebFilterRule> WebFilters { get; set; } = new List<WebFilterRule>();
    public virtual ICollection<UsageLog> UsageLogs { get; set; } = new List<UsageLog>();
}

/// <summary>
/// Association entre un profil et un appareil (MAC Address)
/// </summary>
public class ProfileDevice
{
    [Key]
    public int Id { get; set; }
    
    public int ProfileId { get; set; }
    
    [Required]
    [MaxLength(17)]
    public string MacAddress { get; set; } = string.Empty;
    
    [MaxLength(100)]
    public string? DeviceName { get; set; }
    
    [MaxLength(50)]
    public string? IpAddress { get; set; }
    
    /// <summary>
    /// Appareil actuellement en ligne
    /// </summary>
    [NotMapped]
    public bool IsOnline { get; set; } = false;
    
    /// <summary>
    /// Appareil actuellement bloqué
    /// </summary>
    [NotMapped]
    public bool IsBlocked { get; set; } = false;
    
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    [ForeignKey("ProfileId")]
    public virtual ChildProfile? Profile { get; set; }
}

/// <summary>
/// Plage horaire autorisée pour un profil
/// </summary>
public class TimeSchedule
{
    [Key]
    public int Id { get; set; }
    
    public int ProfileId { get; set; }
    
    /// <summary>
    /// Jour de la semaine (0 = Dimanche, 6 = Samedi)
    /// </summary>
    [Range(0, 6)]
    public int DayOfWeek { get; set; }
    
    /// <summary>
    /// Heure de début (ex: "08:00")
    /// </summary>
    [Required]
    [MaxLength(5)]
    public string StartTime { get; set; } = "08:00";
    
    /// <summary>
    /// Heure de fin (ex: "21:00")
    /// </summary>
    [Required]
    [MaxLength(5)]
    public string EndTime { get; set; } = "21:00";
    
    /// <summary>
    /// Plage active
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    // Navigation
    [ForeignKey("ProfileId")]
    public virtual ChildProfile? Profile { get; set; }
}

/// <summary>
/// Règle de filtrage web pour un profil
/// </summary>
public class WebFilterRule
{
    [Key]
    public int Id { get; set; }
    
    public int ProfileId { get; set; }
    
    /// <summary>
    /// Type de filtre
    /// </summary>
    public WebFilterType FilterType { get; set; }
    
    /// <summary>
    /// Nom de la catégorie ou domaine
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Value { get; set; } = string.Empty;
    
    /// <summary>
    /// Description du filtre
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }
    
    /// <summary>
    /// Filtre actif
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// ID du groupe Pi-hole associé (si applicable)
    /// </summary>
    public int? PiholeGroupId { get; set; }
    
    // Navigation
    [ForeignKey("ProfileId")]
    public virtual ChildProfile? Profile { get; set; }
}

/// <summary>
/// Types de filtres web disponibles
/// </summary>
public enum WebFilterType
{
    /// <summary>
    /// Catégorie prédéfinie (ex: adult, social-media, gaming)
    /// </summary>
    Category = 0,
    
    /// <summary>
    /// Domaine spécifique (ex: facebook.com)
    /// </summary>
    Domain = 1,
    
    /// <summary>
    /// Liste de blocage Pi-hole
    /// </summary>
    PiholeBlocklist = 2,
    
    /// <summary>
    /// Regex personnalisé
    /// </summary>
    Regex = 3
}

/// <summary>
/// Catégories de filtrage prédéfinies
/// </summary>
public static class WebFilterCategories
{
    public static readonly Dictionary<string, WebFilterCategoryInfo> Categories = new()
    {
        ["adult"] = new WebFilterCategoryInfo
        {
            Name = "Contenu Adulte",
            Description = "Sites pornographiques et contenu pour adultes",
            Icon = "fa-ban",
            Color = "#ff4757",
            PiholeListUrl = "https://raw.githubusercontent.com/StevenBlack/hosts/master/alternates/porn/hosts"
        },
        ["social-media"] = new WebFilterCategoryInfo
        {
            Name = "Réseaux Sociaux",
            Description = "Facebook, Instagram, TikTok, Snapchat, etc.",
            Icon = "fa-users",
            Color = "#3b5998",
            Domains = new[] { "facebook.com", "instagram.com", "tiktok.com", "snapchat.com", "twitter.com", "x.com" }
        },
        ["gaming"] = new WebFilterCategoryInfo
        {
            Name = "Jeux Vidéo",
            Description = "Sites de jeux et plateformes gaming",
            Icon = "fa-gamepad",
            Color = "#9b59b6",
            Domains = new[] { "steampowered.com", "epicgames.com", "roblox.com", "minecraft.net", "twitch.tv" }
        },
        ["streaming"] = new WebFilterCategoryInfo
        {
            Name = "Streaming Vidéo",
            Description = "YouTube, Netflix, Disney+, etc.",
            Icon = "fa-film",
            Color = "#e74c3c",
            Domains = new[] { "youtube.com", "netflix.com", "disneyplus.com", "primevideo.com", "hulu.com" }
        },
        ["gambling"] = new WebFilterCategoryInfo
        {
            Name = "Jeux d'Argent",
            Description = "Sites de paris et casinos en ligne",
            Icon = "fa-dice",
            Color = "#f39c12",
            PiholeListUrl = "https://raw.githubusercontent.com/StevenBlack/hosts/master/alternates/gambling/hosts"
        },
        ["malware"] = new WebFilterCategoryInfo
        {
            Name = "Malware & Phishing",
            Description = "Sites malveillants et tentatives de phishing",
            Icon = "fa-virus",
            Color = "#c0392b",
            PiholeListUrl = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts"
        }
    };
}

public class WebFilterCategoryInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "fa-filter";
    public string Color { get; set; } = "#00d9ff";
    public string[]? Domains { get; set; }
    public string? PiholeListUrl { get; set; }
}

/// <summary>
/// Log d'utilisation pour suivre le temps d'écran
/// </summary>
public class UsageLog
{
    [Key]
    public int Id { get; set; }
    
    public int ProfileId { get; set; }
    
    /// <summary>
    /// Date du log
    /// </summary>
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;
    
    /// <summary>
    /// Minutes utilisées ce jour
    /// </summary>
    public int MinutesUsed { get; set; } = 0;
    
    /// <summary>
    /// Nombre de connexions
    /// </summary>
    public int ConnectionCount { get; set; } = 0;
    
    /// <summary>
    /// Nombre de blocages automatiques
    /// </summary>
    public int BlockCount { get; set; } = 0;
    
    /// <summary>
    /// Dernier appareil actif
    /// </summary>
    [MaxLength(17)]
    public string? LastActiveDevice { get; set; }
    
    // Navigation
    [ForeignKey("ProfileId")]
    public virtual ChildProfile? Profile { get; set; }
}

/// <summary>
/// Statut en temps réel d'un profil enfant
/// </summary>
public class ProfileStatus
{
    public int ProfileId { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Color { get; set; } = "#00d9ff";
    
    /// <summary>
    /// Statut global du profil
    /// </summary>
    public ProfileAccessStatus Status { get; set; }
    
    /// <summary>
    /// Raison du blocage si applicable
    /// </summary>
    public string? BlockReason { get; set; }
    
    /// <summary>
    /// Au moins un appareil en ligne
    /// </summary>
    public bool IsOnline { get; set; }
    
    /// <summary>
    /// Minutes restantes aujourd'hui (-1 si illimité)
    /// </summary>
    public int RemainingMinutes { get; set; } = -1;
    
    /// <summary>
    /// Pourcentage du temps utilisé (0-100)
    /// </summary>
    public int UsagePercentage { get; set; } = 0;
    
    /// <summary>
    /// Prochaine plage horaire autorisée
    /// </summary>
    public string? NextAllowedTime { get; set; }
    
    /// <summary>
    /// Heure de fin de la plage actuelle
    /// </summary>
    public string? CurrentSlotEnds { get; set; }
    
    /// <summary>
    /// Liste des appareils du profil
    /// </summary>
    public List<ProfileDeviceStatus> Devices { get; set; } = new();
}

public class ProfileDeviceStatus
{
    public string MacAddress { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? IpAddress { get; set; }
    public bool IsOnline { get; set; }
    public bool IsBlocked { get; set; }
}

public enum ProfileAccessStatus
{
    /// <summary>
    /// Accès autorisé
    /// </summary>
    Allowed = 0,
    
    /// <summary>
    /// Bloqué par le planning horaire
    /// </summary>
    BlockedBySchedule = 1,
    
    /// <summary>
    /// Bloqué car temps d'écran dépassé
    /// </summary>
    BlockedByTimeLimit = 2,
    
    /// <summary>
    /// Pause manuelle activée
    /// </summary>
    Paused = 3,
    
    /// <summary>
    /// Profil désactivé
    /// </summary>
    Disabled = 4
}

/// <summary>
/// DTO pour créer/modifier un profil
/// </summary>
public class ChildProfileDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Color { get; set; } = "#00d9ff";
    public int DailyTimeLimitMinutes { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public string BlockedMessage { get; set; } = "L'accès Internet est temporairement désactivé.";
    public List<string> DeviceMacs { get; set; } = new();
    public List<TimeScheduleDto> Schedules { get; set; } = new();
    public List<string> BlockedCategories { get; set; } = new();
    public List<string> BlockedDomains { get; set; } = new();
}

public class TimeScheduleDto
{
    public int DayOfWeek { get; set; }
    public string StartTime { get; set; } = "08:00";
    public string EndTime { get; set; } = "21:00";
    public bool IsEnabled { get; set; } = true;
}
